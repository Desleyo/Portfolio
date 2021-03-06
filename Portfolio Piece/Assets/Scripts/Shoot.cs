using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Shoot : MonoBehaviour
{
    [Header("Main references")]
    [SerializeField] PlayerController playerControls;
    [SerializeField] Camera cam;
    [SerializeField] Animator animator;
    [SerializeField] Animator reload;
    [SerializeField] GameObject armMesh;

    [Header("Flash & Bullet hole references")]
    [SerializeField] GameObject flash;
    [SerializeField] GameObject bulletHit;
    [SerializeField] Transform muzzle;
    [SerializeField] Transform bulletHitParent;
    [SerializeField] LayerMask wallLayer;
    [SerializeField] LayerMask enemyLayer;

    [Header("Damage values")]
    [SerializeField] int bodyshotDamage;
    [SerializeField] int headshotDamage;
    [SerializeField] int wallbangDamageReducer;
    [SerializeField] float autoFireRate;
    [SerializeField] float semiFireRate;

    [Header("Ammo & Reload variables")]
    [SerializeField] TextMeshProUGUI bulletCountText;
    [SerializeField] public int currentBulletCount;
    [SerializeField] int maxBulletCount;
    [SerializeField] float reloadTime;
    [HideInInspector] public bool isReloading;

    [Header("Recoil & Spread variables")]
    [SerializeField] TextMeshProUGUI spreadStatusText;
    [SerializeField] float recoilSpreadCorrection;
    [SerializeField] float[] spreadPatternX;
    [SerializeField] float[] spreadPatternY;
    [SerializeField] float normalSpread;
    [SerializeField] float movingSpread;
    [SerializeField] float jumpingSpread;
    bool spreadEnabled = true;
    Vector3 spreading;
    int patternIndex;

    [Space, SerializeField] float recoilValueX;
    [SerializeField] float recoilValueY;
    [SerializeField] float autoRecoilResetTime;
    [SerializeField] float semiRecoilResetTime;
    float recoilResetTime;

    [Header("FireMode variables")]
    [SerializeField] TextMeshProUGUI fireModeText;
    [SerializeField] float fireModeCooldown;
    bool canSwitchFireMode = true;

    //Some private variables
    [HideInInspector] public bool fullAuto = true;
    bool isShooting;
    bool nextShot;
    float nextTimeToShoot;
    RaycastHit previousHit;

    int subtractFromDamage;
    int currentHeadshotDamage;
    int currentBodyshotDamage;

    private void Update()
    {
        CheckInput();

        if (currentBulletCount > 0 && !isReloading && nextTimeToShoot < Time.time)
        {
            if (isShooting || nextShot)
            {
                currentBulletCount--;
                patternIndex++;

                SetupRaycast();

                float fireRate = fullAuto ? autoFireRate : semiFireRate;
                nextTimeToShoot = Time.time + 1f / fireRate;
                nextShot = false;
            }
        }

        if(recoilResetTime > 0)
        {
            recoilResetTime -= Time.deltaTime;
            if (recoilResetTime <= 0)
            {
                patternIndex = 0;
                recoilResetTime = Mathf.Infinity;
            }
        }
    }

    void CheckInput()
    {
        //Check if the player wants to fire a bullet
        isShooting = fullAuto ? Input.GetButton("Fire1") : Input.GetButtonDown("Fire1");

        //Check if the player wants to reload, or if the gun is empty
        if (Input.GetButtonDown("Reload") && !isReloading && nextTimeToShoot < Time.time && currentBulletCount != maxBulletCount || currentBulletCount == 0 && !isReloading && nextTimeToShoot < Time.time)
        {
            reload.gameObject.SetActive(true);
            armMesh.SetActive(false);

            reload.SetTrigger("Reload");
            StartCoroutine(Reload(reloadTime));
            isReloading = true;
        }

        //Display the bulletcount on screen
        if (!isReloading)
            bulletCountText.text = currentBulletCount + "|" + maxBulletCount;

        //Check if the player wants to switch firemodes
        if (Input.GetButton("FireMode") && canSwitchFireMode && !isShooting && !isReloading)
        {
            fullAuto = !fullAuto;
            fireModeText.text = fullAuto ? "V : Auto" : "V : Semi";
            canSwitchFireMode = false;
            StartCoroutine(FireModeCooldown(fireModeCooldown));
        }

        //Check if the player wants to (De)Activate the spread
        if (Input.GetButtonDown("SetSpread"))
        {
            spreadEnabled = !spreadEnabled;
            spreadStatusText.text = spreadEnabled ? "B : Spread on" : "B : Spread off";
            spreadStatusText.color = spreadEnabled ? Color.green : Color.red;
        }

        //check for input in between chambering rounds
        if (nextTimeToShoot > Time.time && Input.GetButtonDown("Fire1"))
            nextShot = true;
        else if (nextTimeToShoot > Time.time && Input.GetButtonUp("Fire1"))
            nextShot = false;
    }

    public void SetupRaycast()
    {
        animator.SetTrigger("Shoot");

        InstantiateFlash();

        //Add recoil to camera according to the fireMode
        float condition = spreadPatternX[patternIndex - 1];
        float recoilX = condition > 0 ? recoilValueX : condition < 0 ? -recoilValueX : 0;
        playerControls.AddRecoil(recoilX, recoilValueY);
        recoilResetTime = fullAuto ? autoRecoilResetTime : semiRecoilResetTime;

        //Add spread if enabled, but make sure first bullet accuracy is perserved
        spreading = new Vector3(0, 0, 0);
        if (spreadEnabled)
        {
            if(patternIndex != 1 || playerControls.isMoving || !playerControls.CanJump())
            {
                //Check how much spread is needed according to the movement of the player
                float spread = !playerControls.CanJump() ? jumpingSpread : playerControls.isMoving ? movingSpread : normalSpread;
                if (playerControls.isCrouching)
                    spread /= 2;

                float randomX;
                float randomY;
                if (!fullAuto)
                    randomX = randomY = Random.Range(-spread, spread);
                else
                {
                    if (spread == normalSpread || spread == normalSpread / 2)
                    {
                        randomX = spreadPatternX[patternIndex - 1] + Random.Range(-spread, spread);
                        randomY = spreadPatternY[patternIndex - 1];
                    }
                    else
                        randomX = randomY  = Random.Range(-spread, spread);
                }

                spreading = new Vector3(randomX / recoilSpreadCorrection, randomY / recoilSpreadCorrection, 0);
            }
        }

        ShootRaycast(cam.transform.position, new Vector3(0, 0, 0), spreading, false);
    }

    void ShootRaycast(Vector3 point, Vector3 previousPoint, Vector3 spread, bool wallBanged)
    {
        if (wallBanged)
        {
            subtractFromDamage += Mathf.RoundToInt(wallbangDamageReducer * Vector3.Distance(point, previousPoint));
        }
        else
        {
            currentHeadshotDamage = headshotDamage;
            currentBodyshotDamage = bodyshotDamage;
        }

        if (Physics.Raycast(point, cam.transform.TransformDirection(Vector3.forward + spread), out RaycastHit hit))
        {
            string tag = hit.collider.tag;

            //Update damage values 
            currentHeadshotDamage -= subtractFromDamage;
            currentBodyshotDamage -= subtractFromDamage;

            //Damage according to bodyShot values
            if (tag == "Body" && bodyshotDamage - subtractFromDamage > 0)
                hit.collider.GetComponent<EnemyHealth>().TakeDamage(currentBodyshotDamage, false, wallBanged);
            //Damage according to headShot values
            else if (tag == "Head" && headshotDamage - subtractFromDamage > 0)
                hit.collider.GetComponentInParent<EnemyHealth>().TakeDamage(currentHeadshotDamage, true, wallBanged);
            //Damage the wall
            else if (hit.collider.CompareTag("Wall") && bodyshotDamage - subtractFromDamage > 0)
                hit.collider.GetComponentInParent<WallHealth>().TakeDamage(currentBodyshotDamage);

            Debug.DrawRay(point, cam.transform.TransformDirection(Vector3.forward + spread), Color.blue, 10f);

            ImpactHole(tag, hit.point, hit);
            CheckForWallbang(hit, spread);
        }
        else
            subtractFromDamage = 0;
    }

    void CheckForWallbang(RaycastHit hit, Vector3 spread)
    {
        GameObject hitObject = hit.collider.gameObject;

        //Check if a new raycast needs to be fired from the hit point
        if (hitObject.layer == 7 || hitObject.layer == 8)
        {   
            //Shoot a raycast to find the other side of the wallbangable object
            Vector3 offset = hit.point + cam.transform.TransformDirection(new Vector3(0, 0, 7.5f));
            int layerValue = hitObject.layer == 8 ? enemyLayer : wallLayer;

            if (Physics.Raycast(offset, cam.transform.TransformDirection(-Vector3.forward), out RaycastHit backwardsHit, Mathf.Infinity, layerValue))
            {
                ImpactHole(backwardsHit.collider.tag, backwardsHit.point, backwardsHit);
                ShootRaycast(backwardsHit.point, hit.point, spread, true);
            }

                Debug.DrawRay(offset, cam.transform.TransformDirection(-Vector3.forward), Color.red, 10f);
        }
        else
            subtractFromDamage = 0;
    }

    void ImpactHole(string tag, Vector3 point, RaycastHit hit)
    {
        int health = tag == "Wall" ? hit.collider.GetComponentInParent<WallHealth>().health : 100;

        //Show impact hole
        if (tag != "Body" && tag != "Head" && tag != "Player" && health > 0)
        {
            Transform parent = tag == "Ground" || tag == "Untagged" ? bulletHitParent : hit.collider.gameObject.transform;
            var hitEffect = Instantiate(bulletHit, point, Quaternion.identity, parent);
            hitEffect.transform.up = hit.normal;

            StartCoroutine(DestroyEffect(5f, hitEffect));
        }
    }

    void InstantiateFlash()
    {
        var effect = Instantiate(flash, muzzle.position, muzzle.rotation);
        effect.GetComponent<Transform>().SetParent(muzzle);
        StartCoroutine(DestroyEffect(.1f, effect));
    }

    IEnumerator DestroyEffect(float time, GameObject effect)
    {
        yield return new WaitForSeconds(time);

        if(effect)
            Destroy(effect);
    }

    IEnumerator Reload(float time)
    {
        yield return new WaitForSeconds(time);

        nextShot = false;
        isReloading = false;

        animator.ResetTrigger("Reload");

        ResetAmmo();

        armMesh.SetActive(true);
        reload.gameObject.SetActive(false);

        StopCoroutine(nameof(Reload));
    }

    IEnumerator FireModeCooldown(float cooldown)
    {
        yield return new WaitForSeconds(cooldown);

        canSwitchFireMode = true;
    }

    public void ResetAmmo()
    {
        currentBulletCount = maxBulletCount;
    }
}
