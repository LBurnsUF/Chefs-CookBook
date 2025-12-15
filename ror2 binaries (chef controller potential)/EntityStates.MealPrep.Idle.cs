// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// EntityStates.MealPrep.Idle
using EntityStates.MealPrep;

public class Idle : MealPrepBaseState
{
	protected override bool enableInteraction => true;
}
