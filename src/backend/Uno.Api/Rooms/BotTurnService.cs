using Uno.GameEngine;

namespace Uno.Api.Rooms;

public enum BotDifficulty { Easy, Medium, Hard }

public sealed class BotTurnService(RoomStore rooms, RoomRealtimePublisher publisher, ILogger<BotTurnService> logger) : BackgroundService
{
    private readonly Dictionary<string,(Guid Match,Guid Player,DateTimeOffset Deadline,DateTimeOffset Due)> _scheduled = new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(300));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach(var code in rooms.GetActiveRoomCodes())
            {
                try
                {
                    var action=rooms.ExecuteLocked(code,room=>Step(room));
                    if(action is null) continue;
                    await publisher.PublishGameActionAsync(code,action);
                    await publisher.PublishRoomAsync(code);
                    await publisher.PublishGameStateAsync(code);
                }
                catch(Exception ex) when(!stoppingToken.IsCancellationRequested)
                { logger.LogDebug(ex,"Bot turn deferred in {RoomCode}",code); }
            }
        }
    }

    private GameActionEvent? Step(UnoGameRoom room)
    {
        if(room.Game is not { Status:GameStatus.InProgress } game) { _scheduled.Remove(room.Code); return null; }
        var seat=room.Players.FirstOrDefault(p=>p.GamePlayerId==game.CurrentPlayer.Id);
        if(seat is null || rooms.GetBot(seat.Id) is not {} difficulty) { _scheduled.Remove(room.Code); return null; }
        var now=DateTimeOffset.UtcNow;
        if(!_scheduled.TryGetValue(room.Code,out var scheduled) || scheduled.Match!=game.MatchId || scheduled.Player!=seat.Id || scheduled.Deadline!=game.TurnDeadlineUtc)
        {
            _scheduled[room.Code]=(game.MatchId,seat.Id,game.TurnDeadlineUtc,now.AddMilliseconds(900));
            return null;
        }
        if(now<scheduled.Due || now>=game.TurnDeadlineUtc || (game.PlayerAwaitingUnoCallId!=game.CurrentPlayer.Id && game.UnoCallDeadlineUtc is {} grace && now<grace)) return null;
        _scheduled.Remove(room.Code);
        var engine=new UnoGameEngine();
        var player=game.CurrentPlayer;
        var hand=player.Hand;
        var legal=hand.Where(c=>
            (hand.Count>1 || c.Type==CardType.Number) &&
            (!game.CanPassAfterDraw || c==game.JustDrawnCardForCurrentPlayer) &&
            (game.PendingDrawAmount>0
                ? game.RuleSet==UnoRuleSet.House && c.Type==game.PendingDrawType
                : c.Type is CardType.Wild or CardType.WildDrawFour || c.Color==game.ActiveColor ||
                  (c.Type==game.TopDiscard.Type && (c.Type!=CardType.Number || c.Number==game.TopDiscard.Number))) &&
            (c.Type!=CardType.WildDrawFour || !hand.Any(h=>h.Color==game.ActiveColor && h.Type is not (CardType.Wild or CardType.WildDrawFour)))).ToArray();
        if(game.IsWildDrawFourChallengePending && game.RuleSet!=UnoRuleSet.House) legal=[];
        GameActionPayload payload;
        if(legal.Length>0)
        {
            var nextIndex=(game.CurrentPlayerIndex+game.TurnDirection+game.Players.Count)%game.Players.Count;
            var threatened=game.Players[nextIndex].Hand.Count<=2;
            int Score(UnoCard c) => (c.Type is CardType.Wild or CardType.WildDrawFour ? -5 : hand.Count(h=>h.Color==c.Color))
                +(difficulty==BotDifficulty.Hard && threatened && c.Type is CardType.Skip or CardType.DrawTwo or CardType.WildDrawFour ? 20:0);
            var card=difficulty==BotDifficulty.Easy?legal[Random.Shared.Next(legal.Length)]:legal.OrderByDescending(Score).First();
            var color=card.Type is CardType.Wild or CardType.WildDrawFour
                ? new[]{CardColor.Red,CardColor.Yellow,CardColor.Green,CardColor.Blue}.OrderByDescending(c=>difficulty==BotDifficulty.Easy?Random.Shared.Next(100):hand.Count(h=>h.Color==c)).First()
                : CardColor.None;
            engine.PlayCard(game,player.Id,card,color);
            if(game.Status==GameStatus.InProgress && game.PlayerAwaitingUnoCallId==player.Id) engine.CallUno(game,player.Id);
            payload=new("CardPlayed",seat.Id,UnoCardResponse.From(card));
        }
        else if(game.PendingDrawAmount>0)
        {
            var before=hand.Count;
            if(game.IsWildDrawFourChallengePending) engine.AcceptWildDrawFour(game,player.Id);
            else engine.AcceptDrawPenalty(game,player.Id);
            payload=new("CardsDrawn",seat.Id,null,hand.Count-before,seat.Id);
        }
        else if(game.CanPassAfterDraw)
        {
            engine.PassAfterDraw(game,player.Id);
            payload=new("CardsDrawn",seat.Id,null,0,seat.Id);
        }
        else
        {
            engine.DrawCard(game,player.Id);
            payload=new("CardDrawn",seat.Id,null,1,seat.Id);
        }
        return publisher.CaptureGameAction(room.Code,payload);
    }
}
