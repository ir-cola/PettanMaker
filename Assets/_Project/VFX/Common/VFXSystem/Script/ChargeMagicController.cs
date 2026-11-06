using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

public class ChargeMagicController : MonoBehaviour
{
    public enum ChargeState
    {
        Idle,
        Charging,
        Converging,
        Released
    }

    [Header("外部トリガ")]
    [Tooltip("ChargeTriggerBase を継承したトリガをここに入れる")]
    [SerializeField] private ChargeTriggerBase triggerSource;

    [Header("Orbit")]
    [SerializeField] private OrbitLocator orbitA;
    [SerializeField] private OrbitLocator orbitB;

    [Header("VFX - チャージ中の先端光")]
    [Tooltip("VisualEffect が付いたオブジェクト、またはその親を入れる")]
    [SerializeField] private GameObject tipLightAObject;

    [Tooltip("VisualEffect が付いたオブジェクト、またはその親を入れる")]
    [SerializeField] private GameObject tipLightBObject;

    [Header("Particle - チャージ中の中心フレア")]
    [Tooltip("チャージ中ずっと出る中心フレア")]
    [SerializeField] private ParticleSystem chargeCoreFlareParticle;

    [Tooltip("2段階目から追加で出す中心フレア。不要なら空でOK")]
    [SerializeField] private ParticleSystem chargeCoreFlareParticleLevel2;

    [Header("Particle - 2段階目移行時の合図")]
    [Tooltip("チャージ2段階目に入った瞬間に再生するParticle")]
    [SerializeField] private ParticleSystem level2CueParticle;

    [Tooltip("2段階目に入ったらONにする追加トレイル用Locatorなど")]
    [SerializeField] private GameObject[] level2ActiveObjects;

    [Header("Particle - 収束後のフレア")]
    [Tooltip("チャージ1段階目の収束フレア")]
    [SerializeField] private ParticleSystem releaseFlareParticleLevel1;

    [Tooltip("チャージ2段階目の収束フレア")]
    [SerializeField] private ParticleSystem releaseFlareParticleLevel2;

    [Header("攻撃")]
    [SerializeField] private Transform attackSpawnPoint;
    [SerializeField] private GameObject attackPrefabLevel1;
    [SerializeField] private GameObject attackPrefabLevel2;

    [Header("チャージ段階")]
    [Tooltip("この秒数以上チャージしたらLevel2")]
    [SerializeField] private float chargeLevel2Time = 1.5f;

    [Header("タイミング")]
    [SerializeField] private float waitAfterConverge = 0.05f;
    [SerializeField] private float hideTipLightDelay = 0.4f;
    [SerializeField] private float hideReleaseFlareDelay = 0.8f;

    [Header("終了後に軌道を消す")]
    [Tooltip("収束後、トレイル用Locatorを非表示にするまでの時間")]
    [SerializeField] private float hideOrbitDelayAfterConverge = 0.3f;

    public ChargeState CurrentState { get; private set; } = ChargeState.Idle;
    public int CurrentChargeLevel { get; private set; } = 0;

    public float CurrentChargeTime
    {
        get
        {
            if (CurrentState != ChargeState.Charging)
            {
                return 0f;
            }

            return Time.time - chargeStartTime;
        }
    }

    private VisualEffect tipLightA;
    private VisualEffect tipLightB;

    private float chargeStartTime;
    private Coroutine releaseCoroutine;
    private bool level2CuePlayed;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();
        RegisterTrigger();
        ResetEffectImmediate();
    }

    private void OnDisable()
    {
        UnregisterTrigger();
    }

    private void Update()
    {
        UpdateChargeLevelCue();
    }

    private void UpdateChargeLevelCue()
    {
        if (CurrentState != ChargeState.Charging)
        {
            return;
        }

        if (level2CuePlayed)
        {
            return;
        }

        float chargeTime = Time.time - chargeStartTime;

        if (chargeTime < chargeLevel2Time)
        {
            return;
        }

        level2CuePlayed = true;
        CurrentChargeLevel = 2;

        PlayParticle(level2CueParticle);
        PlayParticle(chargeCoreFlareParticleLevel2);
        SetLevel2ObjectsActive(true);
    }

    private void CacheComponents()
    {
        tipLightA = FindVisualEffect(tipLightAObject, "Tip Light A");
        tipLightB = FindVisualEffect(tipLightBObject, "Tip Light B");
    }

    private VisualEffect FindVisualEffect(GameObject targetObject, string label)
    {
        if (targetObject == null)
        {
            return null;
        }

        VisualEffect vfx = targetObject.GetComponent<VisualEffect>();

        if (vfx == null)
        {
            vfx = targetObject.GetComponentInChildren<VisualEffect>(true);
        }

        if (vfx == null)
        {
            Debug.LogWarning(
                $"{label} に VisualEffect が見つかりません。VisualEffect付きのオブジェクト、またはその親を設定してください。",
                targetObject
            );
        }

        return vfx;
    }

    private void RegisterTrigger()
    {
        if (triggerSource == null)
        {
            return;
        }

        triggerSource.OnChargeStarted += BeginCharge;
        triggerSource.OnChargeReleased += ReleaseCharge;
        triggerSource.OnChargeCanceled += CancelCharge;
    }

    private void UnregisterTrigger()
    {
        if (triggerSource == null)
        {
            return;
        }

        triggerSource.OnChargeStarted -= BeginCharge;
        triggerSource.OnChargeReleased -= ReleaseCharge;
        triggerSource.OnChargeCanceled -= CancelCharge;
    }

    public void SetTriggerSource(ChargeTriggerBase newTriggerSource)
    {
        UnregisterTrigger();
        triggerSource = newTriggerSource;
        RegisterTrigger();
    }

    public void BeginCharge()
    {
        if (CurrentState == ChargeState.Converging)
        {
            return;
        }

        CacheComponents();
        StopReleaseCoroutine();

        CurrentState = ChargeState.Charging;
        CurrentChargeLevel = 1;
        level2CuePlayed = false;
        chargeStartTime = Time.time;

        SetOrbitActive(true);

        if (orbitA != null)
        {
            orbitA.ResetOrbit();
        }

        if (orbitB != null)
        {
            orbitB.ResetOrbit();
        }

        SetLevel2ObjectsActive(false);

        PlayVFX(tipLightA);
        PlayVFX(tipLightB);

        PlayParticle(chargeCoreFlareParticle);

        StopAndHideParticle(chargeCoreFlareParticleLevel2);
        StopAndHideParticle(level2CueParticle);
        StopAndHideParticle(releaseFlareParticleLevel1);
        StopAndHideParticle(releaseFlareParticleLevel2);
    }

    public void ReleaseCharge()
    {
        if (CurrentState != ChargeState.Charging)
        {
            return;
        }

        float chargeTime = Time.time - chargeStartTime;
        CurrentChargeLevel = GetChargeLevel(chargeTime);

        CurrentState = ChargeState.Converging;

        if (orbitA != null)
        {
            orbitA.BeginAttract();
        }

        if (orbitB != null)
        {
            orbitB.BeginAttract();
        }

        releaseCoroutine = StartCoroutine(ReleaseSequence(CurrentChargeLevel));
    }

    public void CancelCharge()
    {
        StopReleaseCoroutine();
        ResetEffectImmediate();
    }

    public void ResetEffectImmediate()
    {
        CurrentState = ChargeState.Idle;
        CurrentChargeLevel = 0;
        level2CuePlayed = false;

        if (orbitA != null)
        {
            orbitA.ResetOrbit();
        }

        if (orbitB != null)
        {
            orbitB.ResetOrbit();
        }

        SetOrbitActive(false);
        SetLevel2ObjectsActive(false);

        StopAndHideVFX(tipLightA);
        StopAndHideVFX(tipLightB);

        StopAndHideParticle(chargeCoreFlareParticle);
        StopAndHideParticle(chargeCoreFlareParticleLevel2);
        StopAndHideParticle(level2CueParticle);
        StopAndHideParticle(releaseFlareParticleLevel1);
        StopAndHideParticle(releaseFlareParticleLevel2);
    }

    private IEnumerator ReleaseSequence(int chargeLevel)
    {
        while (!BothOrbitsConverged())
        {
            yield return null;
        }

        yield return new WaitForSeconds(waitAfterConverge);

        StopVFX(tipLightA);
        StopVFX(tipLightB);

        StopParticle(chargeCoreFlareParticle);
        StopParticle(chargeCoreFlareParticleLevel2);

        ParticleSystem selectedFlare = GetReleaseFlareParticle(chargeLevel);
        PlayParticle(selectedFlare);

        SpawnAttack(chargeLevel);

        yield return new WaitForSeconds(hideOrbitDelayAfterConverge);

        SetOrbitActive(false);
        SetLevel2ObjectsActive(false);

        yield return new WaitForSeconds(hideTipLightDelay);

        HideVFX(tipLightA);
        HideVFX(tipLightB);

        HideParticle(chargeCoreFlareParticle);
        HideParticle(chargeCoreFlareParticleLevel2);
        HideParticle(level2CueParticle);

        yield return new WaitForSeconds(hideReleaseFlareDelay);

        HideParticle(selectedFlare);

        CurrentState = ChargeState.Released;
        CurrentState = ChargeState.Idle;

        releaseCoroutine = null;
    }

    private int GetChargeLevel(float chargeTime)
    {
        return chargeTime >= chargeLevel2Time ? 2 : 1;
    }

    private ParticleSystem GetReleaseFlareParticle(int chargeLevel)
    {
        if (chargeLevel >= 2 && releaseFlareParticleLevel2 != null)
        {
            return releaseFlareParticleLevel2;
        }

        return releaseFlareParticleLevel1;
    }

    private bool BothOrbitsConverged()
    {
        bool aDone = orbitA == null || orbitA.IsConverged;
        bool bDone = orbitB == null || orbitB.IsConverged;

        return aDone && bDone;
    }

    private void SpawnAttack(int chargeLevel)
    {
        if (attackSpawnPoint == null)
        {
            return;
        }

        GameObject prefab = chargeLevel >= 2 ? attackPrefabLevel2 : attackPrefabLevel1;

        if (prefab == null)
        {
            return;
        }

        Instantiate(prefab, attackSpawnPoint.position, attackSpawnPoint.rotation);
    }

    private void SetOrbitActive(bool active)
    {
        if (orbitA != null)
        {
            orbitA.gameObject.SetActive(active);
        }

        if (orbitB != null)
        {
            orbitB.gameObject.SetActive(active);
        }
    }

    private void SetLevel2ObjectsActive(bool active)
    {
        if (level2ActiveObjects == null)
        {
            return;
        }

        for (int i = 0; i < level2ActiveObjects.Length; i++)
        {
            if (level2ActiveObjects[i] == null)
            {
                continue;
            }

            level2ActiveObjects[i].SetActive(active);
        }
    }

    private void PlayVFX(VisualEffect vfx)
    {
        if (vfx == null)
        {
            return;
        }

        vfx.gameObject.SetActive(true);
        vfx.Reinit();
        vfx.Play();
    }

    private void StopVFX(VisualEffect vfx)
    {
        if (vfx == null)
        {
            return;
        }

        vfx.Stop();
    }

    private void HideVFX(VisualEffect vfx)
    {
        if (vfx == null)
        {
            return;
        }

        vfx.gameObject.SetActive(false);
    }

    private void StopAndHideVFX(VisualEffect vfx)
    {
        if (vfx == null)
        {
            return;
        }

        vfx.Stop();
        vfx.gameObject.SetActive(false);
    }

    private void PlayParticle(ParticleSystem particle)
    {
        if (particle == null)
        {
            return;
        }

        particle.gameObject.SetActive(true);
        particle.Clear(true);
        particle.Play(true);
    }

    private void StopParticle(ParticleSystem particle)
    {
        if (particle == null)
        {
            return;
        }

        particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void HideParticle(ParticleSystem particle)
    {
        if (particle == null)
        {
            return;
        }

        particle.gameObject.SetActive(false);
    }

    private void StopAndHideParticle(ParticleSystem particle)
    {
        if (particle == null)
        {
            return;
        }

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particle.gameObject.SetActive(false);
    }

    private void StopReleaseCoroutine()
    {
        if (releaseCoroutine == null)
        {
            return;
        }

        StopCoroutine(releaseCoroutine);
        releaseCoroutine = null;
    }
}