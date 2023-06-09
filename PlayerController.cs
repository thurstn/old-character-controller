using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    PlayerInput _playerInput;
    CharacterController _characterController;

    enum PlayerStates { GROUND, AIR, WALLRUN, WALLCLIMB, SWIM };
    PlayerStates _currentState = PlayerStates.GROUND;

    // Variables to store player movement values
    Vector2 _currentMovementInput;
    Vector3 _currentMovement;
    Vector3 _appliedMovement;
    Vector3 _finalMovement;
    bool _isMovementPressed;

    // Constants
    float _rotationFactorPerFrame = 10f;
    float _walkSpeed = 20f;

    float _gravity = -9.8f;

    // Jumping variables
    bool _isJumpPressed;
    float _initialJumpVelocity;
    float _maxJumpHeight = 4f;
    float _maxJumpTime = 0.75f;
    bool _isJumping = false;

    // Slopes
    float _groundRayDistance = 3f;
    private RaycastHit _slopeHit;

    // Wallrun
    public LayerMask _whatIsWall, _whatIsGround;
    float _wallCheckDistance = 1f;
    RaycastHit _leftWallHit, _rightWallHit;
    bool _wallLeft, _wallRight;
    bool _checkForWallRun = true;
    IEnumerator _wallExitCoroutine;

    bool _useCameraRelativeMovement = true;



    // Awake is called earlier than Start() in Unity's event lifecycle
    private void Awake()
    {
        // initially set reference variables
        _playerInput = new PlayerInput();
        _characterController = GetComponent<CharacterController>();

        // set the player input callbacks
        _playerInput.CharacterControls.Move.started += OnMovementInput;
        _playerInput.CharacterControls.Move.canceled += OnMovementInput;
        _playerInput.CharacterControls.Move.performed += OnMovementInput; // 'performed' is used to handle analogue inputs on controllers (in this case for the left stick)
        _playerInput.CharacterControls.Jump.started += OnJump;
        _playerInput.CharacterControls.Jump.canceled += OnJump;

        SetupJumpVariables();
    }

    void SetupJumpVariables()
    {
        float timeToApex = _maxJumpTime / 2;
        _gravity = (-2 * _maxJumpHeight) / Mathf.Pow(timeToApex, 2);
        _initialJumpVelocity = (2 * _maxJumpHeight) / timeToApex;
    }

    void OnJump(InputAction.CallbackContext context)
    {
        _isJumpPressed = context.ReadValueAsButton();
    }

    void OnMovementInput(InputAction.CallbackContext context)
    {
        _currentMovementInput = context.ReadValue<Vector2>();
        _isMovementPressed = _currentMovementInput.x != 0 || _currentMovementInput.y != 0;
    }

    // Update is called once per frame
    void Update()
    {
        Debug.DrawRay(transform.position, transform.forward, Color.yellow);
        switch (_currentState)
        {
            case PlayerStates.GROUND:
                HandleGroundState();
                // check state switch conditions
                if (!_characterController.isGrounded)
                    _currentState = PlayerStates.AIR;
                break;

            case PlayerStates.AIR:
                CheckForSideWall();
                HandleAirState();
                // check state switch conditions
                if (_characterController.isGrounded)
                    _currentState = PlayerStates.GROUND;
                if ((_wallLeft || _wallRight) && _currentMovementInput.y > 0)
                    _currentState = PlayerStates.WALLRUN;
                StartWallRun();
                break;

            case PlayerStates.WALLRUN:
                CheckForSideWall();
                HandleWallRunState();
                // check state switch conditions
                if (_characterController.isGrounded)
                    _currentState = PlayerStates.GROUND;
                if ((!_wallLeft && !_wallRight) || _currentMovementInput.y <= 0)
                    _currentState = PlayerStates.AIR;
                break;

            case PlayerStates.WALLCLIMB:
                HandleWallClimbState();
                // check state switch conditions
                if (_characterController.isGrounded)
                    _currentState = PlayerStates.GROUND;
                break;

            case PlayerStates.SWIM:
                HandleSwimState();
                break;
        }
    }

    // method converts player movement vector to align with camera rotation, e.g. makes player forward the same as camera forward direction
    Vector3 ConvertMovementToCameraSpace(Vector3 vectorToRotate)
    {
        // store the y value of the original vectorToRotate
        float currentYValue = vectorToRotate.y;

        // get forward and right direction vectors of the camera
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;
        // remove the y values to ignore upward/downward camera angles
        cameraForward.y = 0;
        cameraRight.y = 0;
        // re-normalize both vectors so the each have a magnitude of 1
        cameraForward = cameraForward.normalized;
        cameraRight = cameraRight.normalized;

        // rotate the x and z vectorToRotate values to camera space
        Vector3 cameraForwardZProduct = vectorToRotate.z * cameraForward;
        Vector3 cameraRightXProduct = vectorToRotate.x * cameraRight;

        // the sum of both values is the Vector3 in camera space
        Vector3 vectorRotatedToCameraSpace = cameraForwardZProduct + cameraRightXProduct;
        vectorRotatedToCameraSpace.y = currentYValue;
        return vectorRotatedToCameraSpace;
    }

    // method converts player movement vector to align with player's current rotation (mainly used for wallrunning)
    Vector3 ConvertMovementToCurrentRotation(Vector3 vectorToRotate)
    {
        float currentYValue = vectorToRotate.y;

        Vector3 playerForward = transform.forward;
        Vector3 playerRight = transform.right;

        playerForward.y = 0;
        playerRight.y = 0;

        playerForward = playerForward.normalized;
        playerRight = playerRight.normalized;

        Vector3 PlayerForwardZProduct = vectorToRotate.z * playerForward;
        Vector3 PlayerRightXProduct = vectorToRotate.x * playerRight;

        Vector3 vectorRotatedToRotation = PlayerForwardZProduct + PlayerRightXProduct;
        vectorRotatedToRotation.y = currentYValue;
        return vectorRotatedToRotation;
    }

    void HandleJump()
    {
        if (!_isJumping && _characterController.isGrounded && _isJumpPressed)
        {
            _isJumping = true;
            _currentMovement.y = _initialJumpVelocity;
            _appliedMovement.y = _initialJumpVelocity;
        }
        else if (!_isJumpPressed && _isJumping && _characterController.isGrounded)
        {
            _isJumping = false;
        }
    }

    void HandleRotation()
    {
        Vector3 positionToLookAt;
        // The change in position the character should point to
        positionToLookAt.x = _finalMovement.x;
        positionToLookAt.y = 0.0f;
        positionToLookAt.z = _finalMovement.z;
        // The current rotation of the character
        Quaternion currentRotation = transform.rotation;

        if (_isMovementPressed)
        {
            // Creates a new rotation based on where the player is currently pressing
            Quaternion targetRotation = Quaternion.LookRotation(positionToLookAt);
            transform.rotation = Quaternion.Slerp(currentRotation, targetRotation, _rotationFactorPerFrame * Time.deltaTime);
            
        }

    }

    void HandleGravity()
    {
        bool isFalling = _currentMovement.y <= 0.0f || !_isJumpPressed; // the "!isJumpPressed" allows for variable jump height based on how long the input is held
        float fallMultiplier = 2.0f;
        if (_characterController.isGrounded) // ensure character is properly grounded
        {
            _currentMovement.y = _gravity;
            _appliedMovement.y = _gravity;
        }
        else if (isFalling) // apply additional gravity after reaching apex of jump
        {
            float previousYVelocity = _currentMovement.y;
            _currentMovement.y = _currentMovement.y + (_gravity * fallMultiplier * Time.deltaTime);
            _appliedMovement.y = Mathf.Max((previousYVelocity + _currentMovement.y) * 0.5f, -30.0f);
        }
        else // apply when character is not grounded
        {
            float previousYVelocity = _currentMovement.y;
            _currentMovement.y = _currentMovement.y + (_gravity * Time.deltaTime);
            _appliedMovement.y = Mathf.Max((previousYVelocity + _currentMovement.y) * 0.5f, -30.0f);
        }
    }


    void HandleGroundState()
    {
        _useCameraRelativeMovement = true;
        if (_wallExitCoroutine != null)
        {
            StopCoroutine(_wallExitCoroutine);
            _checkForWallRun = true;
        }
        HandleRotation();

        _appliedMovement.x = _currentMovementInput.x * _walkSpeed;
        _appliedMovement.z = _currentMovementInput.y * _walkSpeed;

        _finalMovement = _useCameraRelativeMovement ? ConvertMovementToCameraSpace(_appliedMovement) : ConvertMovementToCurrentRotation(_appliedMovement);
        if (OnSteepSlope())
        {
            SteepSlopeMovement();
        }
        _characterController.Move(_finalMovement * Time.deltaTime);

        HandleGravity();

        HandleJump();
    }

    void HandleAirState()
    {
        _useCameraRelativeMovement = true;
        HandleRotation();

        _appliedMovement.x = _currentMovementInput.x * _walkSpeed;
        _appliedMovement.z = _currentMovementInput.y * _walkSpeed;

        _finalMovement = _useCameraRelativeMovement ? ConvertMovementToCameraSpace(_appliedMovement) : ConvertMovementToCurrentRotation(_appliedMovement);
        _characterController.Move(_finalMovement * Time.deltaTime);

        HandleGravity();
    }

    void HandleWallRunState()
    {
        _useCameraRelativeMovement = false;
        _appliedMovement.x = _currentMovementInput.x * _walkSpeed;

        Vector3 wallNormal = _wallRight ? _rightWallHit.normal : _leftWallHit.normal;

        Vector3 wallForward = Vector3.Cross(wallNormal, transform.up);

        // fixes bug where wallrunning only works properly in one direction
        if ((transform.forward - wallForward).magnitude > (transform.forward - -wallForward).magnitude)
        {
            wallForward = -wallForward;
        }
        transform.rotation = Quaternion.LookRotation(wallForward);


        _finalMovement = _useCameraRelativeMovement ? ConvertMovementToCameraSpace(_appliedMovement) : ConvertMovementToCurrentRotation(_appliedMovement);
        _characterController.Move(_finalMovement * Time.deltaTime);

        _appliedMovement.y = -0.5f;

        if (!_isJumping && _isJumpPressed)
        {
            _isJumping = true;
            _characterController.Move(wallNormal * _initialJumpVelocity);
            _checkForWallRun = false;
            _wallExitCoroutine = ExitWallRun();
            StartCoroutine(_wallExitCoroutine);
        }
    }

    void HandleWallClimbState()
    {

    }

    void HandleSwimState()
    {

    }


    // Slope Handling
    private bool OnSteepSlope()
    {
        if (!_characterController.isGrounded) return false;

        if (Physics.Raycast(transform.position, Vector3.down, out _slopeHit, (_characterController.height / 2) + _groundRayDistance))
        {
            float slopeAngle = Vector3.Angle(_slopeHit.normal, Vector3.up);
            if (slopeAngle > _characterController.slopeLimit) return true;
        }
        return false;
    }

    private void SteepSlopeMovement()
    {
        Vector3 slopeDirection = Vector3.up - _slopeHit.normal * Vector3.Dot(Vector3.up, _slopeHit.normal);
        float slideSpeed = _walkSpeed + Time.deltaTime;

        _finalMovement = slopeDirection * -slideSpeed;
        _finalMovement.y -= _slopeHit.point.y;
    }


    // Wall Handling
    void CheckForSideWall()
    {
        if (_checkForWallRun)
        {
            _wallRight = Physics.Raycast(transform.position, transform.right, out _rightWallHit, _wallCheckDistance, _whatIsWall);
            Debug.DrawRay(transform.position, transform.right * _wallCheckDistance);
            _wallLeft = Physics.Raycast(transform.position, -transform.right, out _leftWallHit, _wallCheckDistance, _whatIsWall);
            Debug.DrawRay(transform.position, -transform.right * _wallCheckDistance);
        }
    }

    void StartWallRun()
    {
        _isJumping = false;
        _useCameraRelativeMovement = false;
    }

    IEnumerator ExitWallRun()
    {
        _checkForWallRun = false;
        _wallLeft = _wallRight = false;
        yield return new WaitForSeconds(.3f);
        _checkForWallRun = true;
    }



    // Input Action Handlers
    private void OnEnable()
    {
        _playerInput.CharacterControls.Enable(); // Enable the character controls action map
    }

    private void OnDisable()
    {
        _playerInput.CharacterControls.Disable(); // Disable the character controls action map
    }
}
