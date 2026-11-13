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

    [Header("Parametry fizyczne (Zwiêkszony autorytet manewru)")]
    public float maxThrust = 24000f;
    public float maxGimbalAngle = 16f;     // Wiêkszy zakres wychylenia dyszy dla pewnego obrotu
    public float maxRcsTorque = 4500f;     // Mocniejszy RCS do inicjacji i wyhamowania flipu

    [Header("Parametry startowe (Belly Flop)")]
    public float minStartHeight = 80f;
    public float maxStartHeight = 105f;
    public float maxStartHorizontalOffset = 10f;
    public float minFlipAngle = 30f;
    public float maxFlipAngle = 50f;

    private float currentGimbalAngle = 0f;
    private float previousThrust = 0f;
    private float previousCosTheta = 1f;
    private float fuel = 1.0f;
    private const float FuelBurnRate = 0.0004f;

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        float startX = landingPad.position.x + Random.Range(-maxStartHorizontalOffset, maxStartHorizontalOffset);
        float startY = landingPad.position.y + Random.Range(minStartHeight, maxStartHeight);
        transform.position = new Vector3(startX, startY, landingPad.position.z);

        float direction = Random.value > 0.5f ? 1f : -1f;
        float startAngleZ = Random.Range(minFlipAngle, maxFlipAngle) * direction;
        transform.rotation = Quaternion.Euler(0f, 0f, startAngleZ);

        rb.linearVelocity = new Vector3(Random.Range(-1.5f, 1.5f), -8f, 0f);

        fuel = 1.0f;
        previousThrust = 0f;
        currentGimbalAngle = 0f;
        previousCosTheta = transform.up.y;
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

        CalculateStepReward(thrustFactor);
    }

    private void CalculateStepReward(float thrustFactor)
    {
        Vector3 relPos = landingPad.position - transform.position;
        float dist = new Vector2(relPos.x, relPos.y).magnitude;
        float cosTheta = transform.up.y;

        // 1. Bezpoœredni sygna³ za prostowanie rakiety
        float deltaUpright = cosTheta - previousCosTheta;
        float rFlipProgress = deltaUpright * 15.0f;
        previousCosTheta = cosTheta;

        // Przerwanie epizodu przy przekrêceniu (> ~105 deg)
        if (cosTheta < -0.25f)
        {
            AddReward(-40f);
            EndEpisode();
            return;
        }

        float heightAbovePad = Mathf.Max(1.0f, transform.position.y - landingPad.position.y);
        float wy = Mathf.Min(1.0f, 45.0f / heightAbovePad);

        float rStep = -0.002f;
        float rDist = -0.0008f * dist;
        float rFuel = -0.0005f * thrustFactor;
        float rSpin = -0.003f * Mathf.Abs(rb.angularVelocity.z);

        float uprightScore = Mathf.Pow(Mathf.Max(0f, cosTheta), 4);
        float rOrient = wy * uprightScore * 0.06f;

        // Kara za ucieczkê w górê
        float rUpward = (rb.linearVelocity.y > 2.0f) ? (-0.015f * rb.linearVelocity.y) : 0f;

        // 2. Aktywne naprowadzanie nad œrodek platformy (X -> 0)
        float horizontalAccuracy = Mathf.Exp(-0.15f * Mathf.Abs(relPos.x));
        float rCentering = horizontalAccuracy * 0.02f;

        // 3. Silna nagroda za wyhamowanie i pionowanie na ostatnich 25 metrach
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

        AddReward(rStep + rDist + rOrient + rFuel + rSpin + rFlipProgress + rUpward + rCentering + rLandingApproach);

        // Granice bezpieczeñstwa (X, Y, Z)
        if (heightAbovePad > 135f || Mathf.Abs(relPos.x) > 45f || Mathf.Abs(relPos.z) > 45f)
        {
            AddReward(-60f);
            EndEpisode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("LandingPad"))
        {
            float cosTheta = transform.up.y;
            float speedY = Mathf.Abs(rb.linearVelocity.y);
            float speedX = Mathf.Abs(rb.linearVelocity.x);

            // Wysoka nagroda (+250) zachêca agenta do podjêcia ryzyka pe³nego l¹dowania
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
            EndEpisode();
        }
        else if (collision.gameObject.CompareTag("Ground"))
        {
            AddReward(-40.0f);
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