// Identitas tim pembuat game (dipakai di menu utama & layar transisi).
public static class Credits
{
    public const string Group = "KELOMPOK 1";
    public const string TeamName = "BUBUR DIADUK";
    public static readonly string[] Members = { "Hans", "Basten", "Nathanell" };

    public static string TeamLine => "Dibuat oleh " + Group + " - " + TeamName;
    public static string MembersLine => string.Join(", ", Members);
}
