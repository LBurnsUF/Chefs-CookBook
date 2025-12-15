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
