namespace ShapeRise.Core
{
    /// <summary>Shape types available in ShapeRise Arena.</summary>
    public enum ShapeType : byte
    {
        Triangle  = 0,
        Square    = 1,
        Rectangle = 2,
        Circle    = 3
    }

    /// <summary>Five colors per game design spec.</summary>
    public enum ShapeColor : byte
    {
        Red    = 0,
        Blue   = 1,
        Green  = 2,
        Yellow = 3,
        Purple = 4
    }

    public enum GameState : byte
    {
        WaitingForPlayers = 0,
        Countdown         = 1,
        Playing           = 2,
        GameOver          = 3
    }

    public enum GameMode : byte
    {
        FFA     = 0,   // Free-for-All (2 or 4 players)
        Team2v2 = 1    // Team mode
    }

    public enum PlayerType : byte
    {
        Human = 0,
        Bot   = 1
    }
}
