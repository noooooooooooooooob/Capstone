using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class Cage : MonoBehaviour
{
    public string correctCreatureTag;
    [Tooltip("ClearSoundMaker 가 태그 기준으로 자동 부여 — 수동 설정 불필요")]
    public int puzzleCageIndex = -1;
    public TextMeshProUGUI label;
    public GameObject door;
    public Transform snapPoint;
    public Vector3 doorCloseRotation = new Vector3(0, 90, 0);
    public float doorCloseDuration = 0.5f;
    public Color idleLabelColor = Color.white;
    public Color wrongLabelColor = Color.red;
    public float wrongFeedbackDuration = 0.35f;

    private GameObject capturedCreature;
    private bool isLocked = false;
    private Coroutine wrongFeedbackRoutine;

    void OnTriggerEnter(Collider other)
    {
        if (isLocked) return;
        if (!other.transform.root.CompareTag(correctCreatureTag))
        {
            ShowWrongFeedback();
            return;
        }

        capturedCreature = other.transform.root.gameObject;

        if (snapPoint != null)
        {
            capturedCreature.transform.position = snapPoint.position;
            capturedCreature.transform.rotation = snapPoint.rotation;
        }
        else
        {
            capturedCreature.transform.position = transform.position;
        }

        NavMeshAgent agent = capturedCreature.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        Rigidbody rb = capturedCreature.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        Animator anim = capturedCreature.GetComponent<Animator>();
        if (anim != null) anim.SetTrigger("Idle");

        FlyingCreature flying = capturedCreature.GetComponent<FlyingCreature>();
        if (flying != null) flying.SetCaged();

        SlimeCreature slime = capturedCreature.GetComponent<SlimeCreature>();
        if (slime != null) slime.SetCaged();

        ScorpionCreature scorpion = capturedCreature.GetComponent<ScorpionCreature>();
        if (scorpion != null) scorpion.SetCaged();

        BoxerCreature boxer = capturedCreature.GetComponent<BoxerCreature>();
        if (boxer != null) boxer.SetCaged();

        if (label != null) label.color = Color.green;
        isLocked = true;
        if (door != null) StartCoroutine(CloseDoor());
        if (ClearSoundMaker.Instance != null)
            ClearSoundMaker.Instance.ReportCageLocked(puzzleCageIndex);
    }

    void OnTriggerExit(Collider other)
    {
        if (isLocked) return;
        if (capturedCreature == null) return;
        if (other.transform.root.gameObject != capturedCreature) return;

        if (label != null) label.color = idleLabelColor;

        NavMeshAgent agent = capturedCreature.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = true;

        Rigidbody rb = capturedCreature.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        FlyingCreature flying = capturedCreature.GetComponent<FlyingCreature>();
        if (flying != null) flying.SetFree();

        SlimeCreature slime = capturedCreature.GetComponent<SlimeCreature>();
        if (slime != null) slime.SetFree();

        ScorpionCreature scorpion = capturedCreature.GetComponent<ScorpionCreature>();
        if (scorpion != null) scorpion.SetFree();

        BoxerCreature boxer = capturedCreature.GetComponent<BoxerCreature>();
        if (boxer != null) boxer.SetFree();

        capturedCreature = null;
    }

    System.Collections.IEnumerator CloseDoor()
    {
        float elapsed = 0f;
        Quaternion startRot = door.transform.localRotation;
        Quaternion endRot = Quaternion.Euler(door.transform.localEulerAngles + doorCloseRotation);

        while (elapsed < doorCloseDuration)
        {
            elapsed += Time.deltaTime;
            door.transform.localRotation = Quaternion.Lerp(startRot, endRot, elapsed / doorCloseDuration);
            yield return null;
        }
        door.transform.localRotation = endRot;
    }

    void ShowWrongFeedback()
    {
        if (label == null) return;
        if (wrongFeedbackRoutine != null)
            StopCoroutine(wrongFeedbackRoutine);
        wrongFeedbackRoutine = StartCoroutine(WrongFeedbackRoutine());
    }

    System.Collections.IEnumerator WrongFeedbackRoutine()
    {
        label.color = wrongLabelColor;
        yield return new WaitForSeconds(wrongFeedbackDuration);
        if (!isLocked) label.color = idleLabelColor;
        wrongFeedbackRoutine = null;
    }
}
