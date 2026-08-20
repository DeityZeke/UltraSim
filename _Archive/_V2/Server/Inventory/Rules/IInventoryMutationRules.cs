namespace UltraSim.Server.Inventory;

public interface IInventoryMutationRules
{
    InventoryMutationRuleDecision Evaluate(in InventoryMutationRuleContext context);
}
