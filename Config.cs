namespace LokitMineBot
{
    public static class Config
    {
        public const string Token = "8432487768:AAFE6pyaviun4nVIunQ-SQurjkyn8T1OJSQ";
        public static readonly long[] AdminChatIds = { 7519663051, 8232333949, 8568313736 };

        public const string DiscordInvite = "https://discord.gg/qrxp9BTbdK";
        public const string VkLink = "https://vk.com/ВАША_ССЫЛКА";

        public static readonly (string Name, string Username, string Role)[] Admins =
        {
            ("Кирилл", "@Kirirus08", "Самый главный создатель — решит баги, поможет чем угодно на сервере"),
            ("Макар", "@Makarus_yt", "Основной создатель проекта — помогает новичкам и всему проекту"),
            ("Я", "@st8sss", "Тех-админ Lokit Mine — фиксит баги, делает проект"),
        };
        public const string TelegramChannel = "через тикеты в этом боте";

        // ИИ (OpenRouter) — ключ через переменную окружения OPENROUTER_KEY
        public const string OpenRouterKey = "";
        public const string AiModel = "meta-llama/llama-3.1-8b-instruct:free";

        // модерация
        public static readonly string[] BadWords = { "дурак", "идиот", "тупой", "бля", "ебать", "сука", "пиздец", "хуй", "мудак", "шлюха", "пидор", "ебал", "ебаш" };
        public const int MaxWarnings = 3;

        // причины бана: 1.1 — не по назначению темы (7 дней), 1.2 — буллинг (2 недели), 1.3 — неккоректные слова (3 недели)
        public static readonly (string Code, string Reason, int Days)[] BanReasons =
        {
            ("1.1", "не по назначению темы", 7),
            ("1.2", "буллинг", 14),
            ("1.3", "неккоректные слова", 21),
        };

        public const string CardNumber = "9112 3880 3789 4180";
        public const string CardHolder = "Елена Харисова";
        public const string CardBank = "Сбербанк";

        public static readonly (string Name, int Price)[] Donations =
        {
            ("Новичок", 0),
            ("Странник", 99),
            ("Житель", 199),
            ("Бродяга", 299),
            ("Поселенец", 449),
            ("Следопыт", 699),
            ("Мастер", 999),
            ("Владыка", 1699),
            ("Лорд", 2999),
            ("Хранитель", 4999),
        };

        public static readonly string[] DonationPerks =
        {
            "Базовые команды, доступ к миру",
            "Все из Новичок + дом в мире, кит Странник",
            "Все из Странник + 2 дома, кит Житель",
            "Все из Житель + полёт в мире, кит Бродяга",
            "Все из Бродяга + 3 дома, кит Поселенец",
            "Все из Поселенец + магазин на сервере, кит Следопыт",
            "Все из Следопыт + приоритетный вход, кит Мастер",
            "Все из Мастер + доступ к креативу, кит Владыка",
            "Все из Владыка + полёт везде, кит Лорд",
            "Все из Лорд + максимальные привилегии, кит Хранитель",
        };
    }
}
