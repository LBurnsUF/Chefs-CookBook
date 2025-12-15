// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EntityStates.MealPrep.CookingToIdle
using EntityStates.MealPrep;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

public class CookingToIdle : MealPrepBaseState
{
	public static string enterSoundString;

	public static string exitSoundString;

	public static float duration;

	public static float dropUpVelocityStrength;

	public static float dropForwardVelocityStrength;

	public static GameObject muzzleflashEffectPrefab;

	public static string muzzleString;

	private bool droppedItem;

	private int itemsDropped;

	private float beatBetweenItems;

	private float timer;

	private static int CookingToIdleStateHash = Animator.StringToHash("YesChef");

	private static int CookingParamHash = Animator.StringToHash("Cooking.playbackRate");

	protected override bool enableInteraction => false;

	public override void OnEnter()
	{
		base.OnEnter();
		Util.PlaySound(enterSoundString, base.gameObject);
		itemsDropped = 0;
		beatBetweenItems = duration * 0.5f / (float)(amountToDrop + 1);
		timer = beatBetweenItems;
		PlayAnimation("Body", CookingToIdleStateHash, CookingParamHash, duration);
		_ = NetworkServer.active;
	}

	private void DropItem()
	{
		if ((bool)muzzleflashEffectPrefab)
		{
			EffectManager.SimpleMuzzleFlash(muzzleflashEffectPrefab, base.gameObject, muzzleString, transmit: false);
		}
		if (NetworkServer.active)
		{
			Transform transform = FindModelChild(muzzleString);
			PickupDropletController.CreatePickupDroplet(pickupToDrop, transform.position, Vector3.up * dropUpVelocityStrength + transform.forward * dropForwardVelocityStrength);
			itemsDropped++;
		}
	}

	public override void OnExit()
	{
		Util.PlaySound(exitSoundString, base.gameObject);
		base.OnExit();
	}

	public override void FixedUpdate()
	{
		base.FixedUpdate();
		if (base.fixedAge > duration * 0.5f && !droppedItem)
		{
			timer += Time.deltaTime;
			if (timer >= beatBetweenItems)
			{
				DropItem();
				timer -= beatBetweenItems;
				if (itemsDropped >= amountToDrop)
				{
					if ((bool)mealPrepSpeechDriver)
					{
						mealPrepSpeechDriver.Talk((int)pickupToDrop.itemIndex);
					}
					droppedItem = true;
				}
			}
		}
		if (base.fixedAge > duration && droppedItem)
		{
			outer.SetNextState(new Idle());
		}
	}
}
