namespace OpaGachi
{
    // Privacy policy, terms of service, what's collected and why, and credits (user, 2026-11-01), shown in
    // Settings and in Help. Plain statements of what the code does; keep them in step with it.
    static class Legal
    {
        public const string Character = "Opa is a Slushie4LiFe.com character and falls under natural copyright laws from its creator.";
        public static string Footer { get { return "OpaGachi " + System.Windows.Forms.Application.ProductVersion + " · © 2026 Slushie4LiFe.com · Opa is a Slushie4LiFe.com character"; } }

        public static readonly string[][] Items =
        {
            new[] { "Privacy policy",
                "OpaGachi runs entirely on your PC. It has no account, no analytics, no ads and no servers of its own, and it never sends anything over the internet. " +
                "Everything it reads stays on this PC, and everything it writes (settings, the log, files you add) is kept in %LOCALAPPDATA%\\OpaGachi. " +
                "It listens only on 127.0.0.1 (this PC), so other devices can't reach it. Uninstall it from Windows Settings > Apps to remove its hooks and program files; " +
                "the uninstaller asks whether to delete your data folder too." },
            new[] { "Terms of service",
                "OpaGachi is free to use and is provided \"as is\", without warranty of any kind. You use it at your own risk; its authors aren't liable for any damage or loss arising from it. " +
                "It isn't made by, affiliated with or endorsed by Anthropic, OpenAI, Google or xAI; Claude, ChatGPT, Codex, Gemini and Grok are trademarks of their owners and are named only to say which apps a pet stands for. " +
                "Your use of those apps stays under their own terms. Sprites and sounds you add must be yours to use. " + Character },
            new[] { "What data is collected and why",
                "Nothing is collected by anyone: OpaGachi reads the following locally, only to animate the pets, and keeps nothing beyond its log and settings.\n" +
                "• Which apps are open and which window is in front (process names and window titles): to know when an AI app opens, closes or is in front.\n" +
                "• The labels of the buttons around an AI desktop app's message box (through Windows accessibility): a Stop button means it's answering, approval or retry buttons mean it needs you or failed. These labels can include bits of the page, such as source names; they're written to the local log for troubleshooting. The text of your conversations isn't read.\n" +
                "• Claude Code's session files and transcripts, and Codex's thread logs: busy or idle, tool names, errors, token counts and plan limits, for the pets and the Usage page.\n" +
                "• Events from hooks you add on the Connections page, and plan limits from the status line relay, sent by those tools to this PC only.\n" +
                "• Files you choose (tie patterns, sprites, sounds) are copied into %LOCALAPPDATA%\\OpaGachi. A .webm sprite is decoded by Microsoft Edge running hidden on this PC; the file isn't uploaded." },
        };

        // Title, text and an optional link.
        public static readonly string[][] Credits =
        {
            new[] { "Created by", "Slushie4LiFe" },
            new[] { "Special thanks", "Quil (Quest) and BigRedSoda (Jdyn)" },
            new[] { "More creations", "Check out other creations at Slushie4LiFe.com", "https://slushie4life.com" },
            new[] { "Opa", Character },
            new[] { "Inter typeface",
                "All text uses Inter by Rasmus Andersson and The Inter Project Authors (rsms.me/inter), licensed under the SIL Open Font License 1.1. " +
                "The license is included as assets/fonts/Inter-OFL.txt." },
            new[] { "Sound effects",
                "The completion sounds come from Pixabay (pixabay.com) and are used under the Pixabay Content License." },
        };
    }
}
