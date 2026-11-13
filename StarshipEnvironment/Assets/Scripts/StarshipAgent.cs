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

    [Header("Parametry fizyczne")]
    public float maxThrust = 24000f;       // Realistyczne TWR ~2.4 przy masie 1000kg
    public float maxGimbalAngle = 10f;     // Realistyczne wychylenie dyszy (+/- 10 deg)
    public float maxRcsTorque = 2000f;     // Moment obrotowy sterów RCS

    private float currentGimbalAngle = 0f;
    private float previousThrust = 0f;
    private float fuel = 1.0f;
    private const float FuelBurnRate = 0.0005f;

    public override void OnEpisodeBegin()
    {
        // 1. Pe³ny reset prêdkoœci liniowej i k¹towej
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 2. Warunki pocz¹tkowe (wysokoœæ 70-90m, ma³y offset poziomy)
        float startX = landingPad.position.x + Random.Range(-5f, 5f);
        float startY = landingPad.position.y + Random.Range(70f, 90f);
        transform.position = new Vector3(startX, startY, landingPad.position.z);

        // 3. £atwy start: prawie pionowo (-15 do +15 stopni)
        float startAngleZ = Random.Range(-15f, 15f);
        transform.rotation = Quaternion.Euler(0f, 0f, startAngleZ);

        // 4. £agodna prêdkoœæ opadania na start
        rb.linearVelocity = new Vector3(0f, -5f, 0f);

        // Reset zmiennych wewnêtrznych
        fuel = 1.0f;
        previousThrust = 0f;
        currentGimbalAngle = 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Dok³adnie 10 zmiennych stanu:
        Vector3 relPos = landingPad.position - transform.position;

        // 1. Delta x (wzglêdna pozycja pozioma)
        sensor.AddObservation(relPos.x);
        // 2. Delta y (wzglêdna wysokoœæ)
        sensor.AddObservation(relPos.y);
        // 3. v_x (prêdkoœæ pozioma)
        sensor.AddObservation(rb.linearVelocity.x);
        // 4. v_y (prêdkoœæ pionowa)
        sensor.AddObservation(rb.linearVelocity.y);
        // 5. u_x = sin(theta) (rzut orientacji na oœ X)
        sensor.AddObservation(transform.up.x);
        // 6. u_y = cos(theta) (rzut orientacji na pion Y: 1.0 = idealny pion)
        sensor.AddObservation(transform.up.y);
        // 7. omega_z (prêdkoœæ k¹towa obrotu)
        sensor.AddObservation(rb.angularVelocity.z);
        // 8. phi_TVC (aktualny k¹t wychylenia dyszy w radianach)
        sensor.AddObservation(currentGimbalAngle * Mathf.Deg2Rad);
        // 9. Poprzedni poziom ci¹gu a_{t-1}
        sensor.AddObservation(previousThrust);
        // 10. Pozosta³y zasób paliwa
        sensor.AddObservation(fuel);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Odbiór 3 ci¹g³ych akcji z przedzia³u [-1, 1]
        float rawThrust = actions.ContinuousActions[0];
        float rawGimbal = actions.ContinuousActions[1];
        float rawRcs = actions.ContinuousActions[2];

        // Mapowanie ci¹gu z [-1, 1] na [0, 1]
        float thrustFactor = Mathf.Clamp01((rawThrust + 1f) / 2f);
        if (fuel <= 0f) thrustFactor = 0f;

        currentGimbalAngle = rawGimbal * maxGimbalAngle;
        float rcsInput = rawRcs;

        // 1. Zastosowanie ci¹gu z wektorowaniem (TVC)
        Quaternion gimbalRotation = Quaternion.Euler(0f, 0f, currentGimbalAngle);
        Vector3 thrustDirection = gimbalRotation * transform.up;
        rb.AddForceAtPosition(thrustDirection * (thrustFactor * maxThrust), enginePivot.position, ForceMode.Force);

        // 2. Zastosowanie momentu obrotowego RCS
        rb.AddTorque(Vector3.forward * (rcsInput * maxRcsTorque), ForceMode.Force);

        // 3. Zu¿ycie paliwa
        fuel = Mathf.Max(0f, fuel - thrustFactor * FuelBurnRate);
        previousThrust = thrustFactor;

        // 4. Krokowa funkcja nagrody
        CalculateStepReward(thrustFactor);
    }

    private void CalculateStepReward(float thrustFactor)
    {
        Vector3 relPos = landingPad.position - transform.position;
        float dist = new Vector2(relPos.x, relPos.y).magnitude;
        float cosTheta = transform.up.y;

        // Natychmiastowe przerwanie epizodu, gdy rakieta przewróci siê w locie (> ~72 deg)
        if (cosTheta < 0.3f)
        {
            SetReward(-50f);
            EndEpisode();
            return;
        }

        // Waga wysokoœciowa
        float heightAbovePad = Mathf.Max(1.0f, transform.position.y - landingPad.position.y);
        float wy = Mathf.Min(1.0f, 35.0f / heightAbovePad);

        // Podstawowe sk³adniki
        float rStep = -0.002f;
        float rDist = -0.001f * dist;
        float rFuel = -0.0005f * thrustFactor;

        // Kara za zbyt szybkie obracanie siê
        float rSpin = -0.005f * Mathf.Abs(rb.angularVelocity.z);

        // Zaostrzona nagroda za pion
        float uprightScore = Mathf.Pow(Mathf.Max(0f, cosTheta), 6);
        float rOrient = wy * uprightScore * 0.08f;

        // Przy podejœciu poni¿ej 15m: nagradzamy ma³¹ prêdkoœæ (zw³aszcza boczn¹) i karzemy za przechy³
        float rLandingApproach = 0f;
        if (heightAbovePad < 15.0f)
        {
            if (cosTheta > 0.85f)
            {
                float speedPenalty = (Mathf.Abs(rb.linearVelocity.y) + Mathf.Abs(rb.linearVelocity.x)) * 0.002f;
                rLandingApproach += 0.015f - speedPenalty;
            }
            float lateralPenalty = Mathf.Abs(rb.linearVelocity.x) * 0.01f;
            float tiltPenalty = (1.0f - Mathf.Max(0f, cosTheta)) * 0.15f;
            float angularPenalty = Mathf.Abs(rb.angularVelocity.z) * 0.02f;
            rLandingApproach -= (lateralPenalty + tiltPenalty + angularPenalty);
        }

        AddReward(rStep + rDist + rOrient + rFuel + rSpin + rLandingApproach);

        // Warunek granicy (Out of bounds)
        if (heightAbovePad > 160f || Mathf.Abs(relPos.x) > 60f)
        {
            SetReward(-100f);
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

            // 1. Pe³ny sukces: miêkkie i pionowe l¹dowanie
            if (speedY < 8.0f && speedX < 3.0f && cosTheta > 0.94f)
            {
                SetReward(150.0f);
            }
            // 2. Strefa poœrednia ("prawie dobrze"): dodatnia nagroda prowadz¹ca sieæ ku idea³owi
            else if (speedY < 14.0f && speedX < 6.0f && cosTheta > 0.80f)
            {
                float quality = (1.0f - (speedY / 14.0f)) * 40f + (cosTheta * 30f);
                SetReward(quality);
            }
            // 3. Twarde lub krzywe rozbicie na platformie
            else
            {
                SetReward(-25.0f);
            }
            EndEpisode();
        }
        else if (collision.gameObject.CompareTag("Ground"))
        {
            SetReward(-50.0f);
            EndEpisode();
        }
    }

    // Sterowanie rêczne z klawiatury
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