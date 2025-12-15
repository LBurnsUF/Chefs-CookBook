// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EntityStates.MealPrep.Cooking
using EntityStates.MealPrep;
using RoR2;
using UnityEngine;

public class Cooking : MealPrepBaseState
{
	public static string enterSoundString;

	public static string exitSoundString;

	public static GameObject cookingEffectPrefab;

	public static float duration;

	public static string muzzleString;

	private static int CookingStateHash = Animator.StringToHash("Cooking");

	private static int CookingParamHash = Animator.StringToHash("Cooking.playbackRate");

	protected override bool enableInteraction => false;

	public override void OnEnter()
	{
		base.OnEnter();
		Util.PlaySound(enterSoundString, base.gameObject);
		if ((bool)cookingEffectPrefab)
		{
			EffectData effectData = new EffectData();
			Transform transform = FindModelChild(muzzleString);
			effectData.origin = transform.position;
			effectData.start = transform.position;
			EffectManager.SpawnEffect(cookingEffectPrefab, effectData, transmit: true);
		}
		PlayAnimation("Body", CookingStateHash, CookingParamHash, duration);
	}

	public override void OnExit()
	{
		Util.PlaySound(exitSoundString, base.gameObject);
		base.OnExit();
	}

	public override void FixedUpdate()
	{
		base.FixedUpdate();
		if (base.fixedAge > duration)
		{
			outer.SetNextState(new CookingToIdle
			{
				pickupsToTake = pickupsToTake,
				pickupToDrop = pickupToDrop,
				amountToDrop = amountToDrop
			});
		}
	}
}

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

// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EntityStates.MealPrep.Idle
using EntityStates.MealPrep;

public class Idle : MealPrepBaseState
{
	protected override bool enableInteraction => true;
}

// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EntityStates.MealPrep.MealPrepBaseState
using System.Collections.Generic;
using EntityStates;
using RoR2;
using RoR2.CharacterSpeech;

public class MealPrepBaseState : BaseState
{
	protected MealPrepController mealPrepController;

	protected MealPrepSpeechDriver mealPrepSpeechDriver;

	public List<PickupIndex> pickupsToTake;

	public PickupIndex pickupToDrop;

	public int amountToDrop = 1;

	protected virtual bool enableInteraction => true;

	public override void OnEnter()
	{
		base.OnEnter();
		mealPrepSpeechDriver = GetComponent<MealPrepSpeechDriver>();
		mealPrepController = GetComponent<MealPrepController>();
		mealPrepController.craftingController.SetAvailable(enableInteraction);
		if (pickupsToTake == null)
		{
			pickupsToTake = new List<PickupIndex>();
		}
	}
}

// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EntityStates.MealPrep.WaitToBeginCooking
using EntityStates.MealPrep;
using RoR2;
using UnityEngine;

public class WaitToBeginCooking : MealPrepBaseState
{
	public static float duration;

	public static string enterSoundString;

	public static string exitSoundString;

	private static int CookingStateHash = Animator.StringToHash("Fidget");

	private static int CookingParamHash = Animator.StringToHash("Cooking.playbackRate");

	protected override bool enableInteraction => false;

	public override void OnEnter()
	{
		base.OnEnter();
		Util.PlaySound(enterSoundString, base.gameObject);
		PlayAnimation("Body", CookingStateHash, CookingParamHash, duration);
	}

	public override void FixedUpdate()
	{
		base.FixedUpdate();
		if (base.fixedAge > duration)
		{
			outer.SetNextState(new Cooking
			{
				pickupsToTake = pickupsToTake,
				pickupToDrop = pickupToDrop,
				amountToDrop = amountToDrop
			});
		}
	}

	public override void OnExit()
	{
		Util.PlaySound(exitSoundString, base.gameObject);
		base.OnExit();
	}
}
