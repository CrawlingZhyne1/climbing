// Transform 자식 앵커(1,2,3,4,5)를 기준으로 절차형 매달림 포즈를 만든다.
// 양쪽 하박의 2는 LeftHandTarget / RightHandTarget에 붙는 최우선 제약이다.
// Body의 1은 HoldingPoint 쪽으로 약하게 끌리는 보정만 받는다.
// Rigidbody2D, Collider2D, Joint2D는 런타임에서 비활성화한다.
// 파츠의 localScale은 변경하지 않는다.
using System;
using UnityEngine;

public sealed class CharacterRagdollView : MonoBehaviour
{
    private struct TwoPointPart
    {
        public Transform Part;
        public Transform Anchor1;
        public Transform Anchor2;
        public float Length;
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
        GameOver,
    }

    [Header("Root References")]
    [SerializeField]
    private Transform holdingPoint;

    [SerializeField]
    private Transform ragdollRoot;

    [SerializeField]
    private Transform leftHandTarget;

    [SerializeField]
    private Transform rightHandTarget;

    [Header("Parts")]
    [SerializeField]
    private Transform body;

    [SerializeField]
    private Transform head;

    [SerializeField]
    private Transform leftUpperArm;

    [SerializeField]
    private Transform leftLowerArm;

    [SerializeField]
    private Transform rightUpperArm;

    [SerializeField]
    private Transform rightLowerArm;

    [SerializeField]
    private Transform leftUpperLeg;

    [SerializeField]
    private Transform leftLowerLeg;

    [SerializeField]
    private Transform rightUpperLeg;

    [SerializeField]
    private Transform rightLowerLeg;

    [Header("Body Hanging")]
    [SerializeField]
    private float bodyDropFromHands = 0.95f;

    [SerializeField]
    private Vector2 bodyRandomOffsetAmplitude = new Vector2(0.08f, 0.05f);

    [SerializeField]
    private float bodyRandomRotationAmplitude = 5f;

    [SerializeField]
    private Vector2 bodyRandomIntervalRange = new Vector2(0.35f, 1.2f);

    [SerializeField]
    private float bodyRandomSmoothTime = 0.35f;

    [Header("Body Anchor 1 Pull")]
    [SerializeField]
    private float bodyAnchor1PullStrength = 0.18f;

    [SerializeField]
    private float bodyAnchor1PullMaxOffset = 0.28f;

    [SerializeField]
    private int bodyReachCorrectionIterations = 3;

    [SerializeField]
    private float bodyReachMargin = 0.03f;

    [Header("Head")]
    [SerializeField]
    private float headLookToHoldingPointStrength = 0.75f;

    [SerializeField]
    private Vector2 headRandomLookAmplitude = new Vector2(0.2f, 0.12f);

    [SerializeField]
    private Vector2 headRandomIntervalRange = new Vector2(0.35f, 1.1f);

    [SerializeField]
    private float headRandomSmoothTime = 0.25f;

    [Header("Arms")]
    [SerializeField]
    private float leftArmBendSign = 1f;

    [SerializeField]
    private float rightArmBendSign = -1f;

    [Header("Legs")]
    [SerializeField]
    private float leftLegBendSign = 1f;

    [SerializeField]
    private float rightLegBendSign = -1f;

    [SerializeField]
    private float legLowerEndDownPull = 0.45f;

    [SerializeField]
    private Vector2 legRandomFootAmplitude = new Vector2(0.15f, 0.08f);

    [SerializeField]
    private Vector2 legRandomIntervalRange = new Vector2(0.45f, 1.4f);

    [SerializeField]
    private float legRandomSmoothTime = 0.4f;

    [Header("Runtime")]
    [SerializeField]
    private bool disablePhysicsComponents = true;

    [SerializeField]
    private bool logMissingSetup = true;

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

    private const float MinSegmentLength = 0.0001f;

    private void Awake()
    {
        InitializeIfNeeded(null);
    }

    public void ResetView(Transform sourceHoldingPoint)
    {
        InitializeIfNeeded(sourceHoldingPoint);
        ResetRandomStates();
        EnterHanging();
    }

    public void EnterHolding()
    {
        EnterHanging();
    }

    public void EnterAiming()
    {
        EnterHanging();
    }

    public void EnterFlying(Vector2 launchVelocity)
    {
        EnterHanging();
    }

    public void EnterHoldSuccess()
    {
        EnterHanging();
    }

    public void EnterGameOver()
    {
        state = ViewState.GameOver;
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

        UpdateRandomStates(Mathf.Max(0f, deltaTime));
        UpdateProceduralHangingPose();
    }

    private void EnterHanging()
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
        UpdateProceduralHangingPose();
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

        result.IsValid =
            result.Part != null
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

        result.Length = Vector2.Distance(anchor1.position, anchor2.position);
        if (result.Length < MinSegmentLength)
        {
            result.IsValid = false;
        }

        return result;
    }

    private bool HasRequiredSetup()
    {
        bool hasSetup =
            holdingPoint != null
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

    private void UpdateProceduralHangingPose()
    {
        if (!HasRequiredSetup())
        {
            return;
        }

        Vector2 leftHand = leftHandTarget.position;
        Vector2 rightHand = rightHandTarget.position;
        Vector2 handCenter = (leftHand + rightHand) * 0.5f;

        UpdateBodyPose(handCenter);
        CorrectBodyToKeepHandsReachable(leftHand, rightHand);
        ApplyBodyAnchor1Pull();

        UpdateArmPose(true, bodyPart.LeftArmAnchor.position, leftHand);
        UpdateArmPose(false, bodyPart.RightArmAnchor.position, rightHand);

        UpdateHeadPose(bodyPart.HeadAnchor.position);
        UpdateLegPose(true, bodyPart.LeftLegAnchor.position);
        UpdateLegPose(false, bodyPart.RightLegAnchor.position);
    }

    private void UpdateBodyPose(Vector2 handCenter)
    {
        Vector2 targetPosition = handCenter + Vector2.down * Mathf.Max(0f, bodyDropFromHands) + bodyRandomOffset.Value;

        body.position = new Vector3(targetPosition.x, targetPosition.y, body.position.z);
        body.rotation = Quaternion.Euler(0f, 0f, bodyRandomRotation.Value);
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

    private void UpdateArmPose(bool isLeft, Vector2 shoulder, Vector2 hand)
    {
        TwoPointPart upper = isLeft ? leftUpperArmPart : rightUpperArmPart;
        TwoPointPart lower = isLeft ? leftLowerArmPart : rightLowerArmPart;
        float bendSign = isLeft ? leftArmBendSign : rightArmBendSign;

        Vector2 elbow = SolveTwoBoneJoint(shoulder, hand, upper.Length, lower.Length, bendSign);

        ApplyPartBetweenAnchors(upper, shoulder, elbow);
        ApplyPartBetweenAnchors(lower, elbow, hand);
    }

    private void UpdateHeadPose(Vector2 neck)
    {
        Vector2 droopDirection = Vector2.down + headRandomLook.Value;
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
        ApplyPartBetweenAnchors(headPart, neck, headAnchor2Target);
    }

    private void UpdateLegPose(bool isLeft, Vector2 hip)
    {
        TwoPointPart upper = isLeft ? leftUpperLegPart : rightUpperLegPart;
        TwoPointPart lower = isLeft ? leftLowerLegPart : rightLowerLegPart;
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

        ApplyPartBetweenAnchors(upper, hip, knee);
        ApplyPartBetweenAnchors(lower, knee, foot);
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