using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField]
    private Rigidbody _body;

    [SerializeField]
    private GameObject _cameraLocker;

    // Standard player movement when not climbing, balancing, etc.
    private Vector2 _move;

    // Forward movement for climbing, balancing, and shimmying
    private float forward;

    [SerializeField]
    private float _speed = 10.0f;

    [SerializeField]
    private float _jumpForce = 10.0f;

    [SerializeField]
    private float _playerHeight = 2.0f;

    [SerializeField]
    private float _playerWidth = 1.0f;

    [SerializeField]
    private float _maxVelocity = 10.0f;

    [Inject]
    private PlayerControls controls;

    [Inject]
    private SignalBus sb;

    // // Variables for dealing with moving up ladders // //
    private LadderInteractionSignal lis;

    [SerializeField]
    private float _climbSpeed = 0.5f;

    private bool _ladderMode;

    private bool _ladderStart;

    private bool _ladderEnd;

    private Vector3 _lerpStart;

    private float _lerpTimer;

    // Start is called before the first frame update
    void Awake()
    {
        controls.Player.Jump.performed += ctx => OnJump();
        controls.Player.Move.performed += ctx => OnMove();
        controls.Player.Climb.performed += ctx => OnClimb();
        controls.Player.Enable();
        _ladderStart = false;
    }

    void Start()
    {
        sb.Subscribe<LadderInteractionSignal>((lis) => { this.lis = lis; _ladderStart = true; });

        // If we have a spawn point in mind, use it
        GameObject spawnPoint = GameObject.FindGameObjectWithTag("Respawn");
        if (spawnPoint != null)
        {
            gameObject.transform.position = spawnPoint.transform.position;
        }
    }

    void Update()
    {
        gameObject.transform.Translate(Vector3.zero);

        // Ladder stuff
        if (_ladderStart)
        {
            Vector3 railStart = lis.ladderRailBottom;
            if (gameObject.transform.position.y > lis.ladderRailBottom.y)
            {
                railStart = new Vector3(lis.ladderRailBottom.x, 
                    gameObject.transform.position.y, lis.ladderRailBottom.z);
            }
            gameObject.transform.position = railStart;
            controls.Player.Jump.Disable();
            controls.Player.Move.Disable();
            controls.Player.Climb.Enable();
            _ladderStart = false;
            _ladderEnd = false;
            _ladderMode = true;
            _body.isKinematic = true;
            forward = 0;
        }
        else if (_ladderMode)
        {
            float movement = forward;
            gameObject.transform.Translate(Vector3.up * (movement * _climbSpeed * Time.deltaTime));
            if (gameObject.transform.position.y < lis.ladderRailBottom.y)
            {
                gameObject.transform.position = lis.ladderRailBottom;
            } else if (gameObject.transform.position.y >= lis.ladderRailTop.y)
            {
                _ladderMode = false;
                _ladderEnd = true;
                _lerpStart = gameObject.transform.position;
                _lerpTimer = 0;
            }
        }
        else if (_ladderEnd)
        {
            _lerpTimer += Time.deltaTime;
            gameObject.transform.position = Vector3.Lerp(_lerpStart, lis.ladderFinish, _lerpTimer);
            if (_lerpTimer >= 1)
            {
                _ladderEnd = false;
                controls.Player.Jump.Enable();
                controls.Player.Move.Enable();
                controls.Player.Climb.Disable();
                _body.isKinematic = false;
            }
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        Vector3 movement = new Vector3(_move.x * _speed * Time.fixedDeltaTime, 0, _move.y * _speed * Time.fixedDeltaTime);
        float result = GetAngleToCamera();

        try { 
            movement = Quaternion.Euler(0, result, 0) * movement;
        } catch (System.Exception e)
        {
            // check if the camera is north or south
            Vector2 playerPos = new Vector2(gameObject.transform.position.x, gameObject.transform.position.z);
            Vector2 cameraPos = new Vector2(Camera.main.transform.position.x, Camera.main.transform.position.z);

            if (playerPos.x < cameraPos.x)
            {
                //movement = Quaternion.Euler(0, 270.00001f, 0) * movement;
            } else
            {
                //movement = Quaternion.Euler(0, 90.00001f, 0) * movement;
            }
        }
        
        // We don't care about velocity if the player is falling
        Vector3 velocityWithoutY = new Vector3(_body.velocity.x, 0, _body.velocity.z);

        if (velocityWithoutY.magnitude <= _maxVelocity)
        {
            _body.AddForce(movement);
        }
    }

    // Gets the angle to the camera from the player relative to player north
    private float GetAngleToCamera()
    {
        Vector2 playerPos = new Vector2(gameObject.transform.position.x, gameObject.transform.position.z);
        Vector2 cameraPos = new Vector2(Camera.main.transform.position.x, Camera.main.transform.position.z);

        // The northpoint (positive X) is a point arbitrarily (effectively infinately) far away
        // This is used to create a triangle with the player and the camera
        // The Z of the player is used for the Z of north, as it's directly north of the player
        Vector2 north = new Vector2(playerPos.x + 100000f, playerPos.y);

        //Constant to rotate the result
        float offset = 90f;

        // Is the camera west of the player?
        if (playerPos.y < cameraPos.y)
        {
            // If so, invert the offset
            offset = offset * -1;

            // And have us check relative to the south (negative X) instead of north
            north = new Vector2(north.x * -1, north.y);
        }

        // Build the triange
        float playerToNorthpoint = Vector2.Distance(playerPos, north);
        float playerToCamera = Vector2.Distance(playerPos, cameraPos);
        float cameraToNorthpoint = Vector2.Distance(cameraPos, north);

        // Law of Cosines simplified for finding cos(C)
        // c^2 = a^2 + b^2 - 2ab * cos(C)
        // cos(C) = (a^2 + b^2 - c^2) / 2ab
        float cosC = ((Mathf.Pow(playerToNorthpoint, 2) + Mathf.Pow(playerToCamera, 2)) 
            - Mathf.Pow(cameraToNorthpoint, 2)) / (2 * playerToNorthpoint * playerToCamera);
        
        // Avoid taking the arc cosine of -1
        float answerInRadians = 0;
        if (cosC != -1f)
        {
            // Take the arc cosine of cos(C)
            answerInRadians = Mathf.Acos(cosC);
        }

        // Convert the answer from radians to degrees
        float answerInDegrees = ((180f / Mathf.PI) * answerInRadians) - offset;
        
        // If the result of the conversion is not a number
        if (float.IsNaN(answerInDegrees))
        {
            // Check if the camera is directly north of the player
            if (playerPos.x < cameraPos.x)
            {
                return 270.0f;
            }
            // Otherwise, it's south of the player
            else
            {
                return 90.0f;
            }
        }

        return answerInDegrees;
    }

    private bool IsGrounded()
    {
        Vector3 position = gameObject.transform.position;
        Vector3 direction = Vector3.down;
        float distance = _playerHeight / 2.0f;

        // See if ground is directly below the player
        if (Physics.Raycast(position, direction, distance))
        {
            Debug.DrawRay(position, direction);
            return true;
        }

        // See if the ground is below points in the north/south/east/west parts of the player
        else if (Physics.Raycast(position + (Vector3.forward * .5f * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (Vector3.back * .5f * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (Vector3.left * .5f * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (Vector3.right * .5f * _playerWidth), direction, distance))
        {
            return true;
        }

        //Check if the ground is below points in the NE/NW/SE/SW parts of the player
        else if (Physics.Raycast(position + (new Vector3(0.25f, 0, 0.25f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(-0.25f, 0, 0.25f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(0.25f, 0, -0.25f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(-0.25f, 0, -0.25f) * _playerWidth), direction, distance))
        {
            return true;
        }

        //Check if the ground is below points in the NE/NW/SE/SW parts of the player
        else if (Physics.Raycast(position + (new Vector3(0.1f, 0, 0.1f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(-0.1f, 0, 0.1f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(0.1f, 0, -0.1f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(-0.1f, 0, -0.1f) * _playerWidth), direction, distance))
        {
            return true;
        }

        // See if the ground is below the north/south/east/west points of the player
        else if (Physics.Raycast(position + (Vector3.forward * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (Vector3.back * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (Vector3.left * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (Vector3.right * _playerWidth), direction, distance))
        {
            return true;
        }

        //Check if the ground is below the NE/NW/SE/SW points of the player
        else if (Physics.Raycast(position + (new Vector3(0.7071f, 0, 0.7071f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(-0.7071f, 0, 0.7071f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(0.7071f, 0, -0.7071f) * _playerWidth), direction, distance))
        {
            return true;
        }
        else if (Physics.Raycast(position + (new Vector3(-0.7071f, 0, -0.7071f) * _playerWidth), direction, distance))
        {
            return true;
        }

        //Guess there's no ground below the player... probs...
        else
        {
            return false;
        }
    }

    private void OnMove()
    {
        Vector2 movement = controls.Player.Move.ReadValue<Vector2>();
        _move.x = movement.x;
        _move.y = movement.y;
    }

    private void OnJump()
    {
        if (IsGrounded())
        {
            _body.AddForce(0, _jumpForce, 0);
        }
    }

    private void OnClimb()
    {
        float movement = controls.Player.Climb.ReadValue<float>();
        forward = movement;
    }
}
