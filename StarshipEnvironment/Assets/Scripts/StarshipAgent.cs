using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

public class StarshipAgent : Agent
{
    [Header("Referencje")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Transform landingPad;
    [SerializeField] private Transform enginePivot;
    [SerializeField] private ParticleSystem engineFlame;

    [Header("Parametry fizyczne")]
    public float maxThrust = 24000f;
    public float maxGimbalAngle = 16f;
    public float maxRcsTorque = 4800f;

    [Header("Parametry startowe (Pe³ny Belly Flop)")]
    public float minStartHeight = 130f;
    public float maxStartHeight = 160f;
    public float maxStartHorizontalOffset = 15f;
    public float minFlipAngle = 65f;
    public float maxFlipAngle = 82f;

    [Header("Prezentacja")]
    public float landingPauseDuration = 2.0f;

    private float currentGimbalAngle = 0f;
    private float previousThrust = 0f;
    private float previousCosTheta = 1f;
    private float fuel = 1.0f;
    private bool isLandingDelay = false;
    private const float FuelBurnRate = 0.0003f;

    private void Start()
    {
        // Wymusza normaln¹ prêdkoœæ czasu (1x) w trybie podgl¹du/prezentacji
        if (!Academy.Instance.IsCommunicatorOn)
        {
            Time.timeScale = 1.0f;
        }
    }

    public override void OnEpisodeBegin()
    {
        CancelInvoke(nameof(EndEpisode));
        isLandingDelay = false;

        // Odblokowanie fizyki na pocz¹tku nowego epizodu
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float startX = landingPad.position.x + Random.Range(-maxStartHorizontalOffset, maxStartHorizontalOffset);
        float startY = landingPad.position.y + Random.Range(minStartHeight, maxStartHeight);
        transform.position = new Vector3(startX, startY, landingPad.position.z);

        float direction = Random.value > 0.5f ? 1f : -1f;
        float startAngleZ = Random.Range(minFlipAngle, maxFlipAngle) * direction;
        transform.rotation = Quaternion.Euler(0f, 0f, startAngleZ);

        rb.linearVelocity = new Vector3(Random.Range(-2f, 2f), -10f, 0f);

        fuel = 1.0f;
        previousThrust = 0f;
        currentGimbalAngle = 0f;
        previousCosTheta = transform.up.y;

        if (engineFlame != null)
        {
            engineFlame.Clear();
            engineFlame.Stop();
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 relPos = landingPad.position - transform.position;

        sensor.AddObservation(relPos.x);
        sensor.AddObservation(relPos.y);
        sensor.AddObservation(rb.linearVelocity.x);
        sensor.AddObservation(rb.linearVelocity.y);
        sensor.AddObservation(transform.up.x);
        sensor.AddObservation(transform.up.y);
        sensor.AddObservation(rb.angularVelocity.z);
        sensor.AddObservation(currentGimbalAngle * Mathf.Deg2Rad);
        sensor.AddObservation(previousThrust);
        sensor.AddObservation(fuel);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (isLandingDelay) return;

        float rawThrust = actions.ContinuousActions[0];
        float rawGimbal = actions.ContinuousActions[1];
        float rawRcs = actions.ContinuousActions[2];

        float thrustFactor = Mathf.Clamp01((rawThrust + 1f) / 2f);
        if (fuel <= 0f) thrustFactor = 0f;

        currentGimbalAngle = rawGimbal * maxGimbalAngle;
        float rcsInput = rawRcs;

        Quaternion gimbalRotation = Quaternion.Euler(0f, 0f, currentGimbalAngle);
        Vector3 thrustDirection = gimbalRotation * transform.up;
        rb.AddForceAtPosition(thrustDirection * (thrustFactor * maxThrust), enginePivot.position, ForceMode.Force);

        rb.AddTorque(Vector3.forward * (rcsInput * maxRcsTorque), ForceMode.Force);

        fuel = Mathf.Max(0f, fuel - thrustFactor * FuelBurnRate);
        previousThrust = thrustFactor;

        UpdateEngineVisuals(thrustFactor);
        CalculateStepReward(thrustFactor);
    }

    private void UpdateEngineVisuals(float thrustFactor)
    {
        if (enginePivot != null)
        {
            enginePivot.localRotation = Quaternion.Euler(0f, 0f, currentGimbalAngle);
        }

        if (engineFlame == null) return;

        // Wymusza skierowanie strumienia cz¹steczek dok³adnie przeciwnie do wektora ci¹gu (w dó³ dyszy)
        engineFlame.transform.rotation = Quaternion.LookRotation(-enginePivot.up);

        var emission = engineFlame.emission;
        var main = engineFlame.main;

        if (!isLandingDelay && fuel > 0f)
        {
            if (!engineFlame.isPlaying) engineFlame.Play();
            emission.rateOverTime = Mathf.Lerp(180f, 700f, thrustFactor);
            main.startSpeed = Mathf.Lerp(25f, 85f, thrustFactor);
            main.startSize = Mathf.Lerp(3.5f, 11.0f, thrustFactor);
        }
        else
        {
            if (engineFlame.isPlaying) engineFlame.Stop();
        }
    }

    private void CalculateStepReward(float thrustFactor)
    {
        Vector3 relPos = landingPad.position - transform.position;
        float dist = new Vector2(relPos.x, relPos.y).magnitude;
        float cosTheta = transform.up.y;
        float heightAbovePad = Mathf.Max(1.0f, transform.position.y - landingPad.position.y);

        float deltaUpright = cosTheta - previousCosTheta;
        float rFlipProgress = (heightAbovePad < 45f) ? (deltaUpright * 4.0f) : 0f;
        previousCosTheta = cosTheta;

        if (cosTheta < -0.35f)
        {
            AddReward(-40f);
            EndEpisode();
            return;
        }

        float wy = Mathf.Min(1.0f, 50.0f / heightAbovePad);

        float rStep = -0.002f;
        float rDist = -0.0006f * dist;
        float rFuel = -0.0004f * thrustFactor;
        float rSpin = -0.003f * Mathf.Abs(rb.angularVelocity.z);

        float uprightScore = Mathf.Pow(Mathf.Max(0f, cosTheta), 4);
        float rOrient = wy * uprightScore * 0.06f;

        float rUpward = (rb.linearVelocity.y > 2.0f) ? (-0.015f * rb.linearVelocity.y) : 0f;

        float desiredVx = Mathf.Clamp(relPos.x * 0.8f, -8f, 8f);
        float vxError = Mathf.Abs(rb.linearVelocity.x - desiredVx);
        float rVelocityMatch = Mathf.Exp(-0.25f * vxError) * 0.03f;

        float rLandingApproach = 0f;
        if (heightAbovePad < 25.0f)
        {
            float safeSpeedScore = Mathf.Clamp01(1.0f - (Mathf.Abs(rb.linearVelocity.y) / 15.0f));
            rLandingApproach += safeSpeedScore * Mathf.Max(0f, cosTheta) * 0.05f;

            float lateralPenalty = Mathf.Abs(rb.linearVelocity.x) * 0.012f;
            float tiltPenalty = (1.0f - Mathf.Max(0f, cosTheta)) * 0.2f;
            float angularPenalty = Mathf.Abs(rb.angularVelocity.z) * 0.02f;
            rLandingApproach -= (lateralPenalty + tiltPenalty + angularPenalty);
        }

        AddReward(rStep + rDist + rOrient + rFuel + rSpin + rFlipProgress + rUpward + rVelocityMatch + rLandingApproach);

        if (heightAbovePad > 190f || Mathf.Abs(relPos.x) > 55f || Mathf.Abs(relPos.z) > 55f)
        {
            AddReward(-60f);
            EndEpisode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isLandingDelay) return;

        if (collision.gameObject.CompareTag("LandingPad"))
        {
            float cosTheta = transform.up.y;
            float speedY = Mathf.Abs(rb.linearVelocity.y);
            float speedX = Mathf.Abs(rb.linearVelocity.x);

            if (speedY < 8.0f && speedX < 3.0f && cosTheta > 0.94f)
            {
                AddReward(250.0f);
            }
            else if (speedY < 14.0f && speedX < 6.0f && cosTheta > 0.80f)
            {
                float quality = (1.0f - (speedY / 14.0f)) * 50f + (cosTheta * 35f);
                AddReward(quality);
            }
            else
            {
                AddReward(-20.0f);
            }

            // Zatrzymuje rakietê w miejscu na platformie, aby zaokr¹glona kapsu³a nie przewraca³a siê na bok
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;

            isLandingDelay = true;
            if (engineFlame != null) engineFlame.Stop();
            Invoke(nameof(EndEpisode), landingPauseDuration);
        }
        else if (collision.gameObject.CompareTag("Ground"))
        {
            AddReward(-40f);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f;
        continuousActions[1] = Input.GetAxis("Horizontal");

        float rcs = 0f;
        if (Input.GetKey(KeyCode.Q)) rcs += 1f;
        if (Input.GetKey(KeyCode.E)) rcs -= 1f;
        continuousActions[2] = rcs;
    }
}