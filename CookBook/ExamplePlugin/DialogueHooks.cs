using BepInEx.Logging;

internal static class ChefDialogueHooks
{
    internal static ManualLogSource _log;
    internal static void Init(ManualLogSource log)
    {
        _log = log;
        //On.EntityStates.MealPrep.MealPrepBaseState.OnEnter += OnEnterChefUI;
        // find whatever hook fires on exiting the ui
    }
    // fill in
    private static void OnEnterChefUI()
    {
        
    }
}
