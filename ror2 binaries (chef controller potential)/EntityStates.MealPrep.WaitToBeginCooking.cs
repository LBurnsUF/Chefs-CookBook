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
