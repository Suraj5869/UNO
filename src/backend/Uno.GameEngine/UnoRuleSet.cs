namespace Uno.GameEngine;

/// <summary>
/// Selects the rule preset used for a Classic UNO game.
/// House rules are a separate preset so optional house-rule behavior can
/// be added explicitly instead of silently changing Official Rules.
/// </summary>
public enum UnoRuleSet
{
    Official = 0,
    House = 1
}
