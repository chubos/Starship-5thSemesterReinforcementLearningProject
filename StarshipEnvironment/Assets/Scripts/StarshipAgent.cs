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
    public float maxThrust = 30000f;       // F_max = 30 kN
    public float maxGimbalAngle = 20f;     // phi_max = 20 stopni
    public float maxRcsTorque = 3000f;     // moment obrotowy klap/RCS

    private float currentGimbalAngle = 0f;
    private float previousThrust = 0f;
    private float fuel = 1.0f;
    private const float FuelBurnRate = 0.0008f;

    public override void OnEpisodeBegin()
    {
        // 1. Reset prêdkoœci fizycznych
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 2. Warunki pocz¹tkowe (wysokoœæ 60-80m, offset poziomy, spadek z przechyleniem)
        float startX = landingPad.position.x + Random.Range(-15f, 15f);
        float startY = landingPad.position.y + Random.Range(60f, 80f);
        transform.position = new Vector3(startX, startY, landingPad.position.z);

        // Pocz¹tkowy k¹t (belly flop: przechy³ o 30 do 75 stopni w lewo lub w prawo)
        float startAngleZ = Random.Range(30f, 75f) * (Random.value > 0.5f ? 1f : -1f);
        transform.rotation = Quaternion.Euler(0f, 0f, startAngleZ);

        // Wstêpna prêdkoœæ opadania
        rb.linearVelocity = new Vector3(Random.Range(-2f, 2f), -15f, 0f);

        // Reset zmiennych wewnêtrznych
        fuel = 1.0f;
        previousThrust = 0f;
        currentGimbalAngle = 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Dok³adnie 10 zmiennych stanu zdefiniowanych w raporcie LaTeX i README:
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
        if (fuel <= 0f) thrustFactor = 0f; // brak paliwa = brak ci¹gu

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

        // 4. Krokowa funkcja nagrody (zgodna ze wzorem z raportu)
        CalculateStepReward(thrustFactor);
    }

    private void CalculateStepReward(float thrustFactor)
    {
        Vector3 relPos = landingPad.position - transform.position;
        float dist = new Vector2(relPos.x, relPos.y).magnitude;
        float cosTheta = transform.up.y;

        // Waga wysokoœciowa w_y = min(1.0, 35.0 / max(Delta y, 1.0))
        float heightAbovePad = Mathf.Max(1.0f, transform.position.y - landingPad.position.y);
        float wy = Mathf.Min(1.0f, 35.0f / heightAbovePad);

        // Sk³adniki ze wzoru r_t:
        float rStep = -0.002f;
        float rDist = -0.001f * dist;
        float rOrient = wy * Mathf.Max(0f, cosTheta) * 0.04f;
        float rFuel = -0.0005f * thrustFactor;

        AddReward(rStep + rDist + rOrient + rFuel);

        // Warunek granicy (Out of bounds)
        if (transform.position.y > 150f || Mathf.Abs(relPos.x) > 50f)
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

            // Warunki bezpiecznego l¹dowania: ma³a prêdkoœæ pionowa i pozioma + pion kad³uba
            if (speedY < 5.0f && speedX < 1.5f && cosTheta > 0.95f)
            {
                SetReward(150.0f); // Sukces
            }
            else
            {
                SetReward(-30.0f); // Zbyt mocne uderzenie lub przewrócenie
            }
            EndEpisode();
        }
        else if (collision.gameObject.CompareTag("Ground"))
        {
            SetReward(-50.0f); // Rozbicie poza platform¹
            EndEpisode();
        }
    }

    // Sterowanie rêczne z klawiatury (do testów przed treningiem)
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        // Spacja: pe³ny ci¹g
        continuousActions[0] = Input.GetKey(KeyCode.Space) ? 1.0f : -1.0f;
        // Strza³ki A/D lub lewo/prawo: gimbal
        continuousActions[1] = Input.GetAxis("Horizontal");
        // Klawisze Q/E: moment obrotowy RCS
        float rcs = 0f;
        if (Input.GetKey(KeyCode.Q)) rcs += 1f;
        if (Input.GetKey(KeyCode.E)) rcs -= 1f;
        continuousActions[2] = rcs;
    }
}