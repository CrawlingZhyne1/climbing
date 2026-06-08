// Transform 앵커를 기준으로 절차형 캐릭터 래그돌 포즈를 만든다.
// 비몸통 파츠는 초기 1->2 앵커 방향 기준 회전각 제한을 적용한다.
// Aiming 상태에서는 PlayerClimbManager가 전달한 pull 값으로 몸통 하강을 시도한다.
// 손끝이 타겟에서 떨어지는 pull 증가는 적용하지 않고 직전 안정 포즈를 유지한다.
// Rigidbody2D, Collider2D, Joint2D는 런타임에서 비활성화한다.
using System;
using UnityEngine;

public sealed class CharacterRagdollView : MonoBehaviour
{
    [Serializable]
    private struct RotationLimit
    {
        public float MinDegrees;
        public float MaxDegrees;

        public RotationLimit(float minDegrees, float maxDegrees)
        {
            MinDegrees = minDegrees;
            MaxDegrees = maxDegrees;
        }
    }

    private struct TwoPointPart
    {
        public Transform Part;
        public Transform Anchor1;
        public Transform Anchor2;
        public float Length;
        public float BaseAnchorAngle;
        public bool IsValid;
    }

    private struct BodyPart
    {
        public Transform Part;
        public Transform HeadAnchor;
        public Transform LeftArmAnchor;
        public Transform RightArmAnchor;
        public Transform LeftLegAnchor;
        public Transform RightLegAnchor;
        public bool IsValid;
    }

    private struct RandomVectorState
    {
        public Vector2 Value;
        public Vector2 Target;
        public Vector2 Velocity;
        public float Timer;
    }

    private struct RandomFloatState
    {
        public float Value;
        public float Target;
        public float Velocity;
        public float Timer;
    }

    private enum ViewState
    {
        None,
        Hanging,
        Aiming,
        ReturningToRest,
        GameOver,
    }

    [Header("Root References")]
    [SerializeField] private Transform holdingPoint;
    [SerializeField] private Transform ragdollRoot;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;

    [Header("Parts")]
    [SerializeField] private Transform body;
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftUpperArm;
    [SerializeField] private Transform leftLowerArm;
    [SerializeField] private Transform rightUpperArm;
    [SerializeField] private Transform rightLowerArm;
    [SerializeField] private Transform leftUpperLeg;
    [SerializeField] private Transform leftLowerLeg;
    [SerializeField] private Transform rightUpperLeg;
    [SerializeField] private Transform rightLowerLeg;

    [Header("Body Hanging")]
    [SerializeField] private float bodyDropFromHands = 0.95f;
    [SerializeField] private Vector2 bodyRandomOffsetAmplitude = new Vector2(0.08f, 0.05f);
    [SerializeField] private float bodyRandomRotationAmplitude = 5f;
    [SerializeField] private Vector2 bodyRandomIntervalRange = new Vector2(0.35f, 1.2f);
    [SerializeField] private float bodyRandomSmoothTime = 0.35f;

    [Header("Body Anchor 1 Pull")]
    [SerializeField] private float bodyAnchor1PullStrength = 0.18f;
    [SerializeField] private float bodyAnchor1PullMaxOffset = 0.28f;
    [SerializeField] private int bodyReachCorrectionIterations = 3;
    [SerializeField] private float bodyReachMargin = 0.03f;

    [Header("Aiming Pull")]
    [Tooltip("PlayerClimbManager가 전달한 드래그 비율에 곱하는 배율. 값이 클수록 적은 드래그로 큰 pull을 만든다.")]
    [SerializeField] private float pullPower = 1f;
    [Tooltip("풀 당김 상태에서 몸통이 기본 매달림 위치보다 추가로 내려가는 거리.")]
    [SerializeField] private float aimingBodyDropOffset = 0.65f;
    [Tooltip("드랍 또는 조준 취소 뒤 기본 포즈로 돌아가는 시간.")]
    [SerializeField] private float dropReturnDuration = 0.3f;
    [Tooltip("드래그 중 목표 pull 값으로 보간되는 시간. 0이면 즉시 목표 pull을 사용한다.")]
    [SerializeField] private float aimingPullSmoothTime = 0.04f;
    [Tooltip("Aiming 중 pull 증가가 한 프레임에서 너무 크게 튀지 않도록 제한하는 초당 증가량.")]
    [SerializeField] private float aimingPullIncreasePerSecond = 3f;
    [Tooltip("Aiming 중 pull 감소가 한 프레임에서 너무 느리게 복귀하지 않도록 제한하는 초당 감소량.")]
    [SerializeField] private float aimingPullDecreasePerSecond = 10f;
    [Tooltip("Aiming 중 손끝이 손 타겟에서 이 거리보다 멀어지면 이번 pull 증가는 적용하지 않는다.")]
    [SerializeField] private float aimingHandDetachTolerance = 0.04f;
    [Tooltip("Aiming pull이 커질수록 몸통/머리 랜덤 흔들림을 줄이는 비율.")]
    [SerializeField] private float aimingRandomDampByPull = 1f;

    [Header("Rotation Limits - Head")]
    [SerializeField] private RotationLimit headRotationLimit = new RotationLimit(-180f, 180f);

    [Header("Rotation Limits - Arms")]
    [SerializeField] private RotationLimit leftUpperArmRotationLimit = new RotationLimit(-180f, 180f);
    [SerializeField] private RotationLimit leftLowerArmRotationLimit = new RotationLimit(-180f, 180f);
    [SerializeField] private RotationLimit rightUpperArmRotationLimit = new RotationLimit(-180f, 180f);
    [SerializeField] private RotationLimit rightLowerArmRotationLimit = new RotationLimit(-180f, 180f);

    [Header("Rotation Limits - Legs")]
    [SerializeField] private RotationLimit leftUpperLegRotationLimit = new RotationLimit(-180f, 180f);
    [SerializeField] private RotationLimit leftLowerLegRotationLimit = new RotationLimit(-180f, 180f);
    [SerializeField] private RotationLimit rightUpperLegRotationLimit = new RotationLimit(-180f, 180f);
    [SerializeField] private RotationLimit rightLowerLegRotationLimit = new RotationLimit(-180f, 180f);

    [Header("Head")]
    [SerializeField] private float headLookToHoldingPointStrength = 0.75f;
    [SerializeField] private Vector2 headRandomLookAmplitude = new Vector2(0.2f, 0.12f);
    [SerializeField] private Vector2 headRandomIntervalRange = new Vector2(0.35f, 1.1f);
    [SerializeField] private float headRandomSmoothTime = 0.25f;

    [Header("Arms")]
    [SerializeField] private float leftArmBendSign = 1f;
    [SerializeField] private float rightArmBendSign = -1f;

    [Header("Legs")]
    [SerializeField] private float leftLegBendSign = 1f;
    [SerializeField] private float rightLegBendSign = -1f;
    [SerializeField] private float legLowerEndDownPull = 0.45f;
    [SerializeField] private Vector2 legRandomFootAmplitude = new Vector2(0.15f, 0.08f);
    [SerializeField] private Vector2 legRandomIntervalRange = new Vector2(0.45f, 1.4f);
    [SerializeField] private float legRandomSmoothTime = 0.4f;

    [Header("Runtime")]
    [SerializeField] private bool disablePhysicsComponents = true;
    [SerializeField] private bool logMissingSetup = true;

    private BodyPart bodyPart;
    private TwoPointPart headPart;
    private TwoPointPart leftUpperArmPart;
    private TwoPointPart leftLowerArmPart;
    private TwoPointPart rightUpperArmPart;
    private TwoPointPart rightLowerArmPart;
    private TwoPointPart leftUpperLegPart;
    private TwoPointPart leftLowerLegPart;
    private TwoPointPart rightUpperLegPart;
    private TwoPointPart rightLowerLegPart;

    private RandomVectorState bodyRandomOffset;
    private RandomFloatState bodyRandomRotation;
    private RandomVectorState headRandomLook;
    private RandomVectorState leftLegRandomFoot;
    private RandomVectorState rightLegRandomFoot;

    private ViewState state = ViewState.None;
    private System.Random random;
    private bool initialized;
    private bool missingSetupLogged;
    private float externalAimingPullRatio;
    private float aimingTargetPullValue;
    private float aimingPullValue;
    private float aimingPullVelocity;
    private float stableAimingPullValue;
    private float returnStartPullValue;
    private float returnElapsed;

    private const float MinSegmentLength = 0.0001f;
    private const float HandCorrectionTolerance = 0.015f;

    private void Awake()
    {
        InitializeIfNeeded(null);
    }

    public void ResetView(Transform sourceHoldingPoint)
    {
        InitializeIfNeeded(sourceHoldingPoint);
        ResetRandomStates();
        externalAimingPullRatio = 0f;
        aimingTargetPullValue = 0f;
        aimingPullValue = 0f;
        aimingPullVelocity = 0f;
        stableAimingPullValue = 0f;
        returnStartPullValue = 0f;
        returnElapsed = 0f;
        EnterHangingImmediate();
    }

    public void EnterHolding()
    {
        BeginReturnToRest();
    }

    public void EnterAiming()
    {
        if (!initialized)
        {
            InitializeIfNeeded(null);
        }

        if (!HasRequiredSetup())
        {
            return;
        }

        state = ViewState.Aiming;
        externalAimingPullRatio = 0f;
        aimingTargetPullValue = 0f;
        aimingPullVelocity = 0f;
        stableAimingPullValue = Mathf.Clamp01(aimingPullValue);
        returnElapsed = 0f;
        returnStartPullValue = aimingPullValue;
    }

    public void SetAimingPull(float pullRatio)
    {
        externalAimingPullRatio = Mathf.Clamp01(pullRatio);

        if (state == ViewState.Aiming)
        {
            aimingTargetPullValue = Mathf.Clamp01(externalAimingPullRatio * Mathf.Max(0f, pullPower));
        }
    }

    public void EnterFlying(Vector2 launchVelocity)
    {
        BeginReturnToRest();
    }

    public void EnterHoldSuccess()
    {
        BeginReturnToRest();
    }

    public void EnterGameOver()
    {
        state = ViewState.GameOver;
        externalAimingPullRatio = 0f;
        aimingTargetPullValue = 0f;
        aimingPullVelocity = 0f;
    }

    public void ManagedUpdate(float deltaTime)
    {
        if (!initialized)
        {
            InitializeIfNeeded(null);
        }

        if (state == ViewState.GameOver)
        {
            return;
        }

        float safeDeltaTime = Mathf.Max(0f, deltaTime);

        UpdatePullState(safeDeltaTime);
        UpdateRandomStates(safeDeltaTime);
        UpdateProceduralHangingPose(safeDeltaTime);
    }

    private void EnterHangingImmediate()
    {
        if (!initialized)
        {
            InitializeIfNeeded(null);
        }

        if (!HasRequiredSetup())
        {
            return;
        }

        state = ViewState.Hanging;
        externalAimingPullRatio = 0f;
        aimingTargetPullValue = 0f;
        aimingPullValue = 0f;
        aimingPullVelocity = 0f;
        stableAimingPullValue = 0f;
        returnElapsed = 0f;
        returnStartPullValue = 0f;
        UpdateProceduralHangingPose(0f);
    }

    private void BeginReturnToRest()
    {
        if (!initialized)
        {
            InitializeIfNeeded(null);
        }

        if (!HasRequiredSetup())
        {
            return;
        }

        externalAimingPullRatio = 0f;
        aimingTargetPullValue = 0f;
        aimingPullVelocity = 0f;
        returnStartPullValue = Mathf.Clamp01(aimingPullValue);
        stableAimingPullValue = returnStartPullValue;
        returnElapsed = 0f;
        state = returnStartPullValue > 0.0001f ? ViewState.ReturningToRest : ViewState.Hanging;
        UpdateProceduralHangingPose(0f);
    }

    private void InitializeIfNeeded(Transform sourceHoldingPoint)
    {
        if (sourceHoldingPoint != null)
        {
            holdingPoint = sourceHoldingPoint;
        }

        if (random == null)
        {
            random = new System.Random(Environment.TickCount ^ GetInstanceID());
        }

        ResolveReferences();

        if (disablePhysicsComponents)
        {
            DisablePhysicsComponentsInRagdoll();
        }

        CacheParts();
        initialized = true;
    }

    private void ResolveReferences()
    {
        Transform root = ResolveCharacterRoot();

        if (holdingPoint == null && root != null)
        {
            holdingPoint = FindDirectChildByName(root, "HoldingPoint");
        }

        if (ragdollRoot == null && root != null)
        {
            ragdollRoot = FindDirectChildByName(root, "CharacterRagdollRoot");
        }

        Transform partRoot = ragdollRoot != null ? ragdollRoot : root;

        if (leftHandTarget == null && root != null)
        {
            leftHandTarget = FindDirectChildByName(root, "LeftHandTarget");
        }

        if (rightHandTarget == null && root != null)
        {
            rightHandTarget = FindDirectChildByName(root, "RightHandTarget");
        }

        if (body == null)
        {
            body = FindChildByName(partRoot, "Body");
        }

        if (head == null)
        {
            head = FindChildByName(partRoot, "Head");
        }

        if (leftUpperArm == null)
        {
            leftUpperArm = FindChildByName(partRoot, "LeftUpperArm");
        }

        if (leftLowerArm == null)
        {
            leftLowerArm = FindChildByName(partRoot, "LeftLowerArm");
        }

        if (rightUpperArm == null)
        {
            rightUpperArm = FindChildByName(partRoot, "RightUpperArm");
        }

        if (rightLowerArm == null)
        {
            rightLowerArm = FindChildByName(partRoot, "RightLowerArm");
        }

        if (leftUpperLeg == null)
        {
            leftUpperLeg = FindChildByName(partRoot, "LeftUpperLeg");
        }

        if (leftLowerLeg == null)
        {
            leftLowerLeg = FindChildByName(partRoot, "LeftLowerLeg");
        }

        if (rightUpperLeg == null)
        {
            rightUpperLeg = FindChildByName(partRoot, "RightUpperLeg");
        }

        if (rightLowerLeg == null)
        {
            rightLowerLeg = FindChildByName(partRoot, "RightLowerLeg");
        }
    }

    private Transform ResolveCharacterRoot()
    {
        if (holdingPoint != null)
        {
            return holdingPoint.parent;
        }

        if (transform.parent != null)
        {
            return transform.parent;
        }

        return transform;
    }

    private void CacheParts()
    {
        bodyPart = BuildBodyPart(body);
        headPart = BuildTwoPointPart(head);
        leftUpperArmPart = BuildTwoPointPart(leftUpperArm);
        leftLowerArmPart = BuildTwoPointPart(leftLowerArm);
        rightUpperArmPart = BuildTwoPointPart(rightUpperArm);
        rightLowerArmPart = BuildTwoPointPart(rightLowerArm);
        leftUpperLegPart = BuildTwoPointPart(leftUpperLeg);
        leftLowerLegPart = BuildTwoPointPart(leftLowerLeg);
        rightUpperLegPart = BuildTwoPointPart(rightUpperLeg);
        rightLowerLegPart = BuildTwoPointPart(rightLowerLeg);
    }

    private BodyPart BuildBodyPart(Transform part)
    {
        BodyPart result = new BodyPart
        {
            Part = part,
            HeadAnchor = FindDirectChildByName(part, "1"),
            LeftArmAnchor = FindDirectChildByName(part, "2"),
            RightArmAnchor = FindDirectChildByName(part, "3"),
            LeftLegAnchor = FindDirectChildByName(part, "4"),
            RightLegAnchor = FindDirectChildByName(part, "5"),
        };

        result.IsValid = result.Part != null
            && result.HeadAnchor != null
            && result.LeftArmAnchor != null
            && result.RightArmAnchor != null
            && result.LeftLegAnchor != null
            && result.RightLegAnchor != null;

        return result;
    }

    private TwoPointPart BuildTwoPointPart(Transform part)
    {
        Transform anchor1 = FindDirectChildByName(part, "1");
        Transform anchor2 = FindDirectChildByName(part, "2");

        TwoPointPart result = new TwoPointPart
        {
            Part = part,
            Anchor1 = anchor1,
            Anchor2 = anchor2,
            IsValid = part != null && anchor1 != null && anchor2 != null,
        };

        if (!result.IsValid)
        {
            return result;
        }

        Vector2 baseVector = anchor2.position - anchor1.position;
        result.Length = baseVector.magnitude;

        if (result.Length < MinSegmentLength)
        {
            result.IsValid = false;
            return result;
        }

        result.BaseAnchorAngle = VectorToAngle(baseVector);
        return result;
    }

    private bool HasRequiredSetup()
    {
        bool hasSetup = holdingPoint != null
            && ragdollRoot != null
            && leftHandTarget != null
            && rightHandTarget != null
            && bodyPart.IsValid
            && headPart.IsValid
            && leftUpperArmPart.IsValid
            && leftLowerArmPart.IsValid
            && rightUpperArmPart.IsValid
            && rightLowerArmPart.IsValid
            && leftUpperLegPart.IsValid
            && leftLowerLegPart.IsValid
            && rightUpperLegPart.IsValid
            && rightLowerLegPart.IsValid;

        if (!hasSetup && logMissingSetup && !missingSetupLogged)
        {
            Debug.LogWarning(
                "CharacterRagdollView requires HoldingPoint, CharacterRagdollRoot, LeftHandTarget, RightHandTarget, Body anchors 1-5, and every other part anchors 1-2."
            );
            missingSetupLogged = true;
        }

        return hasSetup;
    }

    private void UpdatePullState(float deltaTime)
    {
        switch (state)
        {
            case ViewState.Aiming:
                UpdateAimingPull(deltaTime);
                break;

            case ViewState.ReturningToRest:
                UpdateReturningPull(deltaTime);
                break;

            case ViewState.Hanging:
            case ViewState.None:
                aimingTargetPullValue = 0f;
                aimingPullValue = 0f;
                aimingPullVelocity = 0f;
                stableAimingPullValue = 0f;
                break;
        }
    }

    private void UpdateAimingPull(float deltaTime)
    {
        aimingTargetPullValue = Mathf.Clamp01(externalAimingPullRatio * Mathf.Max(0f, pullPower));

        if (aimingPullSmoothTime <= 0f || deltaTime <= 0f)
        {
            aimingPullValue = aimingTargetPullValue;
            aimingPullVelocity = 0f;
            return;
        }

        aimingPullValue = Mathf.SmoothDamp(
            aimingPullValue,
            aimingTargetPullValue,
            ref aimingPullVelocity,
            aimingPullSmoothTime,
            Mathf.Infinity,
            deltaTime
        );

        aimingPullValue = Mathf.Clamp01(aimingPullValue);
    }

    private void UpdateReturningPull(float deltaTime)
    {
        float duration = Mathf.Max(0.0001f, dropReturnDuration);
        returnElapsed += deltaTime;
        float ratio = Mathf.Clamp01(returnElapsed / duration);
        float easedRatio = ratio * ratio * (3f - 2f * ratio);
        aimingPullValue = Mathf.Lerp(returnStartPullValue, 0f, easedRatio);
        stableAimingPullValue = aimingPullValue;

        if (ratio >= 1f)
        {
            aimingPullValue = 0f;
            aimingPullVelocity = 0f;
            stableAimingPullValue = 0f;
            state = ViewState.Hanging;
        }
    }

    private void UpdateRandomStates(float deltaTime)
    {
        bodyRandomOffset.Value = AdvanceRandomVector(
            ref bodyRandomOffset,
            bodyRandomOffsetAmplitude,
            bodyRandomIntervalRange,
            bodyRandomSmoothTime,
            deltaTime
        );
        bodyRandomRotation.Value = AdvanceRandomFloat(
            ref bodyRandomRotation,
            bodyRandomRotationAmplitude,
            bodyRandomIntervalRange,
            bodyRandomSmoothTime,
            deltaTime
        );
        headRandomLook.Value = AdvanceRandomVector(
            ref headRandomLook,
            headRandomLookAmplitude,
            headRandomIntervalRange,
            headRandomSmoothTime,
            deltaTime
        );
        leftLegRandomFoot.Value = AdvanceRandomVector(
            ref leftLegRandomFoot,
            legRandomFootAmplitude,
            legRandomIntervalRange,
            legRandomSmoothTime,
            deltaTime
        );
        rightLegRandomFoot.Value = AdvanceRandomVector(
            ref rightLegRandomFoot,
            legRandomFootAmplitude,
            legRandomIntervalRange,
            legRandomSmoothTime,
            deltaTime
        );
    }

    private void ResetRandomStates()
    {
        bodyRandomOffset = default;
        bodyRandomRotation = default;
        headRandomLook = default;
        leftLegRandomFoot = default;
        rightLegRandomFoot = default;
    }

    private void UpdateProceduralHangingPose(float deltaTime)
    {
        if (!HasRequiredSetup())
        {
            return;
        }

        Vector2 leftHand = leftHandTarget.position;
        Vector2 rightHand = rightHandTarget.position;
        float posePull = ResolvePosePull(leftHand, rightHand, deltaTime);

        ApplyPoseWithPull(leftHand, rightHand, posePull);
    }

    private float ResolvePosePull(Vector2 leftHand, Vector2 rightHand, float deltaTime)
    {
        float requestedPull = Mathf.Clamp01(aimingPullValue);

        if (state != ViewState.Aiming)
        {
            stableAimingPullValue = requestedPull;
            return requestedPull;
        }

        if (requestedPull <= stableAimingPullValue)
        {
            float maxDecrease = GetPullStep(aimingPullDecreasePerSecond, deltaTime, 1f);
            stableAimingPullValue = Mathf.Max(requestedPull, stableAimingPullValue - maxDecrease);
            return stableAimingPullValue;
        }

        float maxIncrease = GetPullStep(aimingPullIncreasePerSecond, deltaTime, 1f);
        float candidatePull = Mathf.Min(requestedPull, stableAimingPullValue + maxIncrease);

        if (CanKeepHandsOnTargetsAtPull(leftHand, rightHand, candidatePull))
        {
            stableAimingPullValue = candidatePull;
        }

        return stableAimingPullValue;
    }

    private float GetPullStep(float valuePerSecond, float deltaTime, float fallback)
    {
        if (valuePerSecond <= 0f || deltaTime <= 0f)
        {
            return Mathf.Max(0f, fallback);
        }

        return Mathf.Max(0f, valuePerSecond) * deltaTime;
    }

    private bool CanKeepHandsOnTargetsAtPull(Vector2 leftHand, Vector2 rightHand, float pull)
    {
        ApplyPoseWithPull(leftHand, rightHand, pull);

        float leftError = Vector2.Distance(leftLowerArmPart.Anchor2.position, leftHand);
        float rightError = Vector2.Distance(rightLowerArmPart.Anchor2.position, rightHand);
        float maxError = Mathf.Max(leftError, rightError);
        return maxError <= Mathf.Max(0f, aimingHandDetachTolerance);
    }

    private void ApplyPoseWithPull(Vector2 leftHand, Vector2 rightHand, float pull)
    {
        Vector2 handCenter = (leftHand + rightHand) * 0.5f;
        UpdateBodyPose(handCenter, pull);

        if (state != ViewState.Aiming)
        {
            CorrectBodyToKeepHandsReachable(leftHand, rightHand);
            ApplyBodyAnchor1Pull();
            CorrectBodyToKeepConstrainedHandsClose(leftHand, rightHand);
        }

        UpdateArmPose(true, bodyPart.LeftArmAnchor.position, leftHand);
        UpdateArmPose(false, bodyPart.RightArmAnchor.position, rightHand);
        UpdateHeadPose(bodyPart.HeadAnchor.position, pull);
        UpdateLegPose(true, bodyPart.LeftLegAnchor.position);
        UpdateLegPose(false, bodyPart.RightLegAnchor.position);
    }

    private void UpdateBodyPose(Vector2 handCenter, float pull)
    {
        float safePull = Mathf.Clamp01(pull);
        float totalDrop = Mathf.Max(0f, bodyDropFromHands) + Mathf.Max(0f, aimingBodyDropOffset) * safePull;
        float randomWeight = GetAimingRandomWeight(safePull);
        Vector2 targetPosition = handCenter + Vector2.down * totalDrop + bodyRandomOffset.Value * randomWeight;
        body.position = new Vector3(targetPosition.x, targetPosition.y, body.position.z);
        body.rotation = Quaternion.Euler(0f, 0f, bodyRandomRotation.Value * randomWeight);
    }

    private float GetAimingRandomWeight(float pull)
    {
        if (state != ViewState.Aiming)
        {
            return 1f;
        }

        float damp = Mathf.Clamp01(aimingRandomDampByPull);
        return Mathf.Lerp(1f, 1f - pull, damp);
    }

    private void CorrectBodyToKeepHandsReachable(Vector2 leftHand, Vector2 rightHand)
    {
        for (int i = 0; i < Mathf.Max(0, bodyReachCorrectionIterations); i++)
        {
            Vector2 correction = Vector2.zero;
            int correctionCount = 0;

            AddShoulderReachCorrection(
                bodyPart.LeftArmAnchor.position,
                leftHand,
                GetArmReach(true),
                ref correction,
                ref correctionCount
            );
            AddShoulderReachCorrection(
                bodyPart.RightArmAnchor.position,
                rightHand,
                GetArmReach(false),
                ref correction,
                ref correctionCount
            );

            if (correctionCount <= 0)
            {
                return;
            }

            Vector2 averageCorrection = correction / correctionCount;
            body.position += new Vector3(averageCorrection.x, averageCorrection.y, 0f);
        }
    }

    private void AddShoulderReachCorrection(
        Vector2 shoulder,
        Vector2 hand,
        float reach,
        ref Vector2 correction,
        ref int correctionCount
    )
    {
        Vector2 toHand = hand - shoulder;
        float distance = toHand.magnitude;
        float safeReach = Mathf.Max(MinSegmentLength, reach - Mathf.Max(0f, bodyReachMargin));

        if (distance <= safeReach || distance < MinSegmentLength)
        {
            return;
        }

        correction += toHand.normalized * (distance - safeReach);
        correctionCount += 1;
    }

    private void CorrectBodyToKeepConstrainedHandsClose(Vector2 leftHand, Vector2 rightHand)
    {
        int iterationCount = Mathf.Max(0, bodyReachCorrectionIterations);

        if (iterationCount <= 0)
        {
            return;
        }

        for (int i = 0; i < iterationCount; i++)
        {
            Vector2 leftEndpoint = UpdateArmPose(true, bodyPart.LeftArmAnchor.position, leftHand);
            Vector2 rightEndpoint = UpdateArmPose(false, bodyPart.RightArmAnchor.position, rightHand);
            Vector2 leftError = leftHand - leftEndpoint;
            Vector2 rightError = rightHand - rightEndpoint;
            Vector2 averageError = (leftError + rightError) * 0.5f;

            if (averageError.magnitude <= HandCorrectionTolerance)
            {
                return;
            }

            body.position += new Vector3(averageError.x, averageError.y, 0f);
        }
    }

    private float GetArmReach(bool isLeft)
    {
        TwoPointPart upper = isLeft ? leftUpperArmPart : rightUpperArmPart;
        TwoPointPart lower = isLeft ? leftLowerArmPart : rightLowerArmPart;
        return upper.Length + lower.Length;
    }

    private void ApplyBodyAnchor1Pull()
    {
        if (holdingPoint == null || bodyPart.HeadAnchor == null)
        {
            return;
        }

        Vector2 bodyAnchor1 = bodyPart.HeadAnchor.position;
        Vector2 toHoldingPoint = (Vector2)holdingPoint.position - bodyAnchor1;
        float distance = toHoldingPoint.magnitude;

        if (distance < MinSegmentLength)
        {
            return;
        }

        float offset = Mathf.Min(distance, Mathf.Max(0f, bodyAnchor1PullMaxOffset)) * Mathf.Max(0f, bodyAnchor1PullStrength);
        Vector2 correction = toHoldingPoint.normalized * offset;
        body.position += new Vector3(correction.x, correction.y, 0f);
    }

    // 팔 2-bone IK 결과에 파츠별 회전 제한을 적용한다.
    private Vector2 UpdateArmPose(bool isLeft, Vector2 shoulder, Vector2 hand)
    {
        TwoPointPart upper = isLeft ? leftUpperArmPart : rightUpperArmPart;
        TwoPointPart lower = isLeft ? leftLowerArmPart : rightLowerArmPart;
        RotationLimit upperLimit = isLeft ? leftUpperArmRotationLimit : rightUpperArmRotationLimit;
        RotationLimit lowerLimit = isLeft ? leftLowerArmRotationLimit : rightLowerArmRotationLimit;
        float bendSign = isLeft ? leftArmBendSign : rightArmBendSign;

        Vector2 elbow = SolveTwoBoneJoint(shoulder, hand, upper.Length, lower.Length, bendSign);
        Vector2 actualElbow = ApplyConstrainedPartBetweenAnchors(upper, shoulder, elbow, upperLimit);
        return ApplyConstrainedPartBetweenAnchors(lower, actualElbow, hand, lowerLimit);
    }

    private void UpdateHeadPose(Vector2 neck, float pull)
    {
        float randomWeight = GetAimingRandomWeight(Mathf.Clamp01(pull));
        Vector2 droopDirection = Vector2.down + headRandomLook.Value * randomWeight;

        if (droopDirection.sqrMagnitude < MinSegmentLength)
        {
            droopDirection = Vector2.down;
        }

        droopDirection.Normalize();

        Vector2 toHoldingPoint = holdingPoint != null ? (Vector2)holdingPoint.position - neck : Vector2.up;

        if (toHoldingPoint.sqrMagnitude < MinSegmentLength)
        {
            toHoldingPoint = Vector2.up;
        }

        toHoldingPoint.Normalize();
        float lookStrength = Mathf.Clamp01(headLookToHoldingPointStrength);
        Vector2 targetDirection = Vector2.Lerp(droopDirection, toHoldingPoint, lookStrength);

        if (targetDirection.sqrMagnitude < MinSegmentLength)
        {
            targetDirection = toHoldingPoint;
        }

        targetDirection.Normalize();
        Vector2 headAnchor2Target = neck + targetDirection * headPart.Length;
        ApplyConstrainedPartBetweenAnchors(headPart, neck, headAnchor2Target, headRotationLimit);
    }

    private void UpdateLegPose(bool isLeft, Vector2 hip)
    {
        TwoPointPart upper = isLeft ? leftUpperLegPart : rightUpperLegPart;
        TwoPointPart lower = isLeft ? leftLowerLegPart : rightLowerLegPart;
        RotationLimit upperLimit = isLeft ? leftUpperLegRotationLimit : rightUpperLegRotationLimit;
        RotationLimit lowerLimit = isLeft ? leftLowerLegRotationLimit : rightLowerLegRotationLimit;
        float bendSign = isLeft ? leftLegBendSign : rightLegBendSign;
        Vector2 randomFoot = isLeft ? leftLegRandomFoot.Value : rightLegRandomFoot.Value;
        float reach = Mathf.Max(MinSegmentLength, upper.Length + lower.Length - bodyReachMargin);
        float downDistance = Mathf.Max(0f, reach + Mathf.Max(0f, legLowerEndDownPull));
        Vector2 desiredFootDirection = Vector2.down + randomFoot;

        if (desiredFootDirection.sqrMagnitude < MinSegmentLength)
        {
            desiredFootDirection = Vector2.down;
        }

        desiredFootDirection.Normalize();
        Vector2 foot = hip + desiredFootDirection * Mathf.Min(downDistance, reach);
        Vector2 knee = SolveTwoBoneJoint(hip, foot, upper.Length, lower.Length, bendSign);
        Vector2 actualKnee = ApplyConstrainedPartBetweenAnchors(upper, hip, knee, upperLimit);
        ApplyConstrainedPartBetweenAnchors(lower, actualKnee, foot, lowerLimit);
    }

    private Vector2 SolveTwoBoneJoint(Vector2 root, Vector2 target, float upperLength, float lowerLength, float bendSign)
    {
        Vector2 toTarget = target - root;
        float distance = toTarget.magnitude;

        if (distance < MinSegmentLength)
        {
            toTarget = Vector2.down;
            distance = MinSegmentLength;
        }

        Vector2 direction = toTarget / distance;
        float maxReach = Mathf.Max(MinSegmentLength, upperLength + lowerLength - 0.0001f);
        float minReach = Mathf.Max(0f, Mathf.Abs(upperLength - lowerLength) + 0.0001f);
        float clampedDistance = Mathf.Clamp(distance, minReach, maxReach);
        float a = Mathf.Max(MinSegmentLength, upperLength);
        float b = Mathf.Max(MinSegmentLength, lowerLength);
        float c = Mathf.Max(MinSegmentLength, clampedDistance);
        float x = (a * a + c * c - b * b) / (2f * c);
        float hSquared = Mathf.Max(0f, a * a - x * x);
        float h = Mathf.Sqrt(hSquared);
        Vector2 basePoint = root + direction * x;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        float sign = bendSign >= 0f ? 1f : -1f;

        return basePoint + perpendicular * h * sign;
    }

    // part의 1번 앵커를 targetAnchor1에 맞추고, 2번 앵커 방향을 초기 방향 기준 제한 범위 안에 둔다.
    private Vector2 ApplyConstrainedPartBetweenAnchors(
        TwoPointPart part,
        Vector2 targetAnchor1,
        Vector2 desiredAnchor2,
        RotationLimit rotationLimit
    )
    {
        if (!part.IsValid)
        {
            return targetAnchor1;
        }

        Vector2 desiredVector = desiredAnchor2 - targetAnchor1;

        if (desiredVector.sqrMagnitude < MinSegmentLength)
        {
            desiredVector = AngleToVector(part.BaseAnchorAngle) * part.Length;
        }

        float desiredAngle = VectorToAngle(desiredVector);
        float constrainedAngle = ClampAngleToLimit(part.BaseAnchorAngle, desiredAngle, rotationLimit);
        Vector2 constrainedVector = AngleToVector(constrainedAngle) * part.Length;
        Vector2 constrainedAnchor2 = targetAnchor1 + constrainedVector;

        ApplyPartBetweenAnchors(part, targetAnchor1, constrainedAnchor2);
        return part.Anchor2.position;
    }

    private void ApplyPartBetweenAnchors(TwoPointPart part, Vector2 targetAnchor1, Vector2 targetAnchor2)
    {
        if (!part.IsValid)
        {
            return;
        }

        Vector2 currentAnchor1 = part.Anchor1.position;
        Vector2 currentAnchor2 = part.Anchor2.position;
        Vector2 currentVector = currentAnchor2 - currentAnchor1;
        Vector2 targetVector = targetAnchor2 - targetAnchor1;

        if (currentVector.sqrMagnitude < MinSegmentLength || targetVector.sqrMagnitude < MinSegmentLength)
        {
            return;
        }

        float angleDelta = Vector2.SignedAngle(currentVector, targetVector);
        part.Part.Rotate(0f, 0f, angleDelta, Space.World);

        Vector3 anchorAfterRotation = part.Anchor1.position;
        Vector3 delta = new Vector3(targetAnchor1.x, targetAnchor1.y, anchorAfterRotation.z) - anchorAfterRotation;
        part.Part.position += delta;
    }

    private float ClampAngleToLimit(float baseAngle, float desiredAngle, RotationLimit rotationLimit)
    {
        float min = Mathf.Min(rotationLimit.MinDegrees, rotationLimit.MaxDegrees);
        float max = Mathf.Max(rotationLimit.MinDegrees, rotationLimit.MaxDegrees);
        float delta = Mathf.DeltaAngle(baseAngle, desiredAngle);
        float clampedDelta = Mathf.Clamp(delta, min, max);
        return baseAngle + clampedDelta;
    }

    private Vector2 AdvanceRandomVector(
        ref RandomVectorState state,
        Vector2 amplitude,
        Vector2 intervalRange,
        float smoothTime,
        float deltaTime
    )
    {
        state.Timer -= deltaTime;

        if (state.Timer <= 0f)
        {
            state.Target = new Vector2(
                RandomRange(-Mathf.Abs(amplitude.x), Mathf.Abs(amplitude.x)),
                RandomRange(-Mathf.Abs(amplitude.y), Mathf.Abs(amplitude.y))
            );
            state.Timer = RandomInterval(intervalRange);
        }

        if (deltaTime <= 0f)
        {
            state.Value = state.Target;
            return state.Value;
        }

        state.Value = Vector2.SmoothDamp(
            state.Value,
            state.Target,
            ref state.Velocity,
            Mathf.Max(0.0001f, smoothTime),
            Mathf.Infinity,
            deltaTime
        );

        return state.Value;
    }

    private float AdvanceRandomFloat(
        ref RandomFloatState state,
        float amplitude,
        Vector2 intervalRange,
        float smoothTime,
        float deltaTime
    )
    {
        state.Timer -= deltaTime;

        if (state.Timer <= 0f)
        {
            state.Target = RandomRange(-Mathf.Abs(amplitude), Mathf.Abs(amplitude));
            state.Timer = RandomInterval(intervalRange);
        }

        if (deltaTime <= 0f)
        {
            state.Value = state.Target;
            return state.Value;
        }

        state.Value = Mathf.SmoothDamp(
            state.Value,
            state.Target,
            ref state.Velocity,
            Mathf.Max(0.0001f, smoothTime),
            Mathf.Infinity,
            deltaTime
        );

        return state.Value;
    }

    private float RandomInterval(Vector2 range)
    {
        float min = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return RandomRange(min, max);
    }

    private float RandomRange(float min, float max)
    {
        if (random == null)
        {
            random = new System.Random(Environment.TickCount ^ GetInstanceID());
        }

        return Mathf.Lerp(min, max, (float)random.NextDouble());
    }

    private void DisablePhysicsComponentsInRagdoll()
    {
        Transform root = ragdollRoot != null ? ragdollRoot : ResolveCharacterRoot();

        if (root == null)
        {
            return;
        }

        Rigidbody2D[] rigidbodies = root.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            if (rigidbodies[i] != null)
            {
                rigidbodies[i].simulated = false;
            }
        }

        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        Joint2D[] joints = root.GetComponentsInChildren<Joint2D>(true);
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
            {
                joints[i].enabled = false;
            }
        }
    }

    private static float VectorToAngle(Vector2 vector)
    {
        return Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
    }

    private static Vector2 AngleToVector(float angleDegrees)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private static Transform FindDirectChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);

            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].name == childName)
            {
                return children[i];
            }
        }

        return null;
    }
}
