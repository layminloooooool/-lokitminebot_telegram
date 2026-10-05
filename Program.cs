using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace LokitMineBot
{
    class Program
    {
        // пользователь нажал кнопку тикета — ждём текст обращения
        static readonly HashSet<long> WaitingTicket = new();

        // пользователь выбирает: личка или тикет
        static readonly HashSet<long> ChoicePending = new();
        static readonly Dictionary<long, string> PendingTicketText = new();

        // живой чат с поддержкой
        static readonly HashSet<long> SupportChat = new();

        // открытые тикеты — игрок пишет, сообщения идут админам
        static readonly HashSet<long> TicketChat = new();

        // приостановленные диалоги
        static readonly HashSet<long> PausedChats = new();

        // админ нажал "Ответить" — ждём его сообщение пользователю
        static readonly Dictionary<long, (long UserChatId, string AdminName)> ReplyToUser = new();

        // список тикетов
        static readonly List<(long ChatId, string Username, string Name, string Text, DateTime Date)> Tickets = new();

        // модерация
        static readonly Dictionary<long, int> Warnings = new();
        static readonly Dictionary<long, DateTime> BanUntil = new();
        static readonly TimeSpan BanDuration = TimeSpan.FromDays(17.5); // 2.5 недели
        static readonly List<(long Id, string Reason, DateTime Banned, DateTime? Unbanned)> BanHistory = new();

        // бан только в тикетах
        static readonly Dictionary<long, DateTime> TicketBanUntil = new();

        // замечания за форму тикета
        static readonly Dictionary<long, int> FormViolations = new();

        // ИИ-ответы, ожидающие отправки админом
        static readonly Dictionary<long, string> PendingAiAnswer = new();

        static async Task<string> AskAi(string prompt)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.OpenRouterKey);
                http.DefaultRequestHeaders.Add("HTTP-Referer", "https://t.me/lokitmine_bot");
                var body = new
                {
                    model = Config.AiModel,
                    messages = new[]
                    {
                        new { role = "system", content = "Ты — дружелюбный администратор Minecraft-сервера LokitMine. Отвечай кратко и по делу, на русском. Помогай игрокам с вопросами о сервере." },
                        new { role = "user", content = prompt }
                    }
                };
                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var resp = await http.PostAsync("https://openrouter.ai/api/v1/chat/completions",
                    new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
                var content = await resp.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(content);
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        static bool HasBadWord(string s)
        {
            var lower = s.ToLower();
            return Config.BadWords.Any(w => lower.Contains(w));
        }

        static async Task Main()
        {
            var client = new TelegramBotClient(Config.Token);

            client.StartReceiving(HandleUpdate, HandleError);

            Console.WriteLine("Бот запущен...");
            await Task.Delay(-1);
        }

        static async Task HandleUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
        {
            if (update.CallbackQuery is { } cb && cb.Message is { } cbMsg)
            {
                var data = cb.Data ?? "";
                var parts = data.Split('_');
                if (parts.Length == 2 && long.TryParse(parts[1], out var targetChatId))
                {
                    if (parts[0] == "reply")
                    {
                        var adminName = cb.From?.Username is { } u ? "@" + u : cb.From?.FirstName ?? "Админ";
                        ReplyToUser[cbMsg.Chat.Id] = (targetChatId, adminName);
                        await client.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                        await client.SendMessage(cbMsg.Chat.Id, "✍️ Режим диалога с игроком. Ваши сообщения уходят ему.\nЧтобы выйти — /стоп.", cancellationToken: ct);
                    }
                    else if (parts[0] == "ticket")
                    {
                        ChoicePending.Remove(targetChatId);
                        if (PendingTicketText.TryGetValue(targetChatId, out var t) && cb.From is { } u2)
                        {
                            PendingTicketText.Remove(targetChatId);
                            Tickets.Add((targetChatId, u2.Username ?? "нет", u2.FirstName, t, DateTime.Now));
                            TicketChat.Add(targetChatId);
                            foreach (var adminId in Config.AdminChatIds)
                            {
                                await client.SendMessage(adminId,
                                    $"🎫 Новый тикет!\n\n👤 От: {u2.FirstName}\n💬 Сообщение:\n{t}",
                                    replyMarkup: new InlineKeyboardMarkup(new[]
                                    {
                                        new[]
                                        {
                                            InlineKeyboardButton.WithCallbackData("✉️ Ответить", $"reply_{targetChatId}"),
                                            InlineKeyboardButton.WithCallbackData("❌ Отказать", $"refuse_{targetChatId}"),
                                        },
                                        new[]
                                        {
                                            InlineKeyboardButton.WithCallbackData("🔒 Закрыть", $"close_{targetChatId}"),
                                        }
                                    }),
                                    cancellationToken: ct);
                            }
                            await client.SendMessage(targetChatId, "✅ Тикет создан! Администратор скоро ответит.", cancellationToken: ct);
                        }
                        await client.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                    }
                    else if (parts[0] == "chat")
                    {
                        ChoicePending.Remove(targetChatId);
                        PendingTicketText.Remove(targetChatId);
                        SupportChat.Add(targetChatId);
                        await client.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                        await client.SendMessage(targetChatId, "💬 Теперь вы в живом чате с поддержкой. Пишите, мы вас слышим. Чтобы выйти, напишите /стоп.", cancellationToken: ct);
                        foreach (var adminId in Config.AdminChatIds)
                        {
                            await client.SendMessage(adminId, "ℹ️ Пользователь перешёл в живой чат с поддержкой.", cancellationToken: ct);
                        }
                    }
                    else if (parts[0] == "refuse")
                    {
                        var adminName = cb.From?.Username is { } u ? "@" + u : cb.From?.FirstName ?? "Админ";
                        ReplyToUser.Remove(cbMsg.Chat.Id);
                        ChoicePending.Remove(targetChatId);
                        PendingTicketText.Remove(targetChatId);
                        SupportChat.Remove(targetChatId);
                        TicketChat.Remove(targetChatId);
                        Tickets.RemoveAll(t => t.ChatId == targetChatId);
                        await client.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                        try
                        {
                            await client.SendMessage(targetChatId, "❌ Ваше обращение отклонено администрацией.", cancellationToken: ct);
                        }
                        catch { }
                        await client.SendMessage(cbMsg.Chat.Id, $"✅ Пользователю отправлен отказ. Тикет закрыт ({adminName}).", cancellationToken: ct);
                    }
                    else if (parts[0] == "donat")
                    {
                        if (int.TryParse(parts[1], out var di) && di >= 0 && di < Config.Donations.Length)
                        {
                            var d = Config.Donations[di];
                            var price = d.Price == 0 ? "Бесплатно" : $"{d.Price}₽";
                            var donatKb = new InlineKeyboardMarkup(Config.Donations.Select((dd, i) =>
                                InlineKeyboardButton.WithCallbackData($"{dd.Name} — {dd.Price}₽", $"donat_{i}")));
                            await client.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                            try
                            {
                                await client.EditMessageText(cbMsg.Chat.Id, cbMsg.MessageId,
                                    $"⭐ {d.Name} — {price}\n\nПлюсы:\n{Config.DonationPerks[di]}",
                                    replyMarkup: donatKb,
                                    cancellationToken: ct);
                            }
                            catch
                            {
                                await client.SendMessage(cbMsg.Chat.Id,
                                    $"⭐ {d.Name} — {price}\n\nПлюсы:\n{Config.DonationPerks[di]}",
                                    cancellationToken: ct);
                            }
                        }
                    }
                    else if (parts[0] == "sendai")
                    {
                        if (Config.AdminChatIds.Contains(cb.From?.Id ?? 0) && PendingAiAnswer.TryGetValue(targetChatId, out var aiText))
                        {
                            PendingAiAnswer.Remove(targetChatId);
                            try
                            {
                                await client.SendMessage(targetChatId, $"✉️ Ответ от поддержки:\n{aiText}", cancellationToken: ct);
                            }
                            catch { }
                            await client.AnswerCallbackQuery(cb.Id, "Ответ отправлен", cancellationToken: ct);
                            await client.SendMessage(cbMsg.Chat.Id, "✅ ИИ-ответ отправлен игроку.", cancellationToken: ct);
                        }
                    }
                    else if (parts[0] == "close")
                    {
                        if (Config.AdminChatIds.Contains(cb.From?.Id ?? 0))
                        {
                            ReplyToUser.Remove(cbMsg.Chat.Id);
                            TicketChat.Remove(targetChatId);
                            SupportChat.Remove(targetChatId);
                            PausedChats.Remove(targetChatId);
                            Tickets.RemoveAll(t => t.ChatId == targetChatId);
                            await client.AnswerCallbackQuery(cb.Id, "Тикет закрыт", cancellationToken: ct);
                            try
                            {
                                await client.SendMessage(targetChatId, "✅ Ваш тикет закрыт. Спасибо за обращение!", cancellationToken: ct);
                            }
                            catch { }
                            await client.SendMessage(cbMsg.Chat.Id, "✅ Тикет закрыт.", cancellationToken: ct);
                        }
                    }
                    else if (parts[0] == "pause")
                    {
                        if (Config.AdminChatIds.Contains(cb.From?.Id ?? 0))
                        {
                            PausedChats.Add(targetChatId);
                            await client.SendMessage(targetChatId, "⏳ Диалог на паузе.", cancellationToken: ct);
                            await client.AnswerCallbackQuery(cb.Id, "Диалог на паузе", cancellationToken: ct);
                        }
                    }
                    else if (parts[0] == "resume")
                    {
                        if (Config.AdminChatIds.Contains(cb.From?.Id ?? 0))
                        {
                            PausedChats.Remove(targetChatId);
                            await client.SendMessage(targetChatId, "▶️ Диалог возобновлён.", cancellationToken: ct);
                            await client.AnswerCallbackQuery(cb.Id, "Диалог возобновлён", cancellationToken: ct);
                        }
                    }
                    try
                    {
                        await client.EditMessageReplyMarkup(cbMsg.Chat.Id, cbMsg.MessageId, replyMarkup: null, cancellationToken: ct);
                    }
                    catch { }
                }
                return;
            }

            if (update.Message is not { } msg) return;
            if (msg.From is null) return;

            var text = msg.Text ?? "";

            // модерация: мат и оскорбления (не для админов)
            if (!Config.AdminChatIds.Contains(msg.From.Id) && text is not null && HasBadWord(text))
            {
                Warnings[msg.From.Id] = Warnings.GetValueOrDefault(msg.From.Id) + 1;
                if (Warnings[msg.From.Id] >= Config.MaxWarnings)
                {
                    BanUntil[msg.From.Id] = DateTime.Now.Add(BanDuration);
                    await client.SendMessage(msg.Chat.Id, "⛔ Вы забанены в боте на 2.5 недели за повторные нарушения.", cancellationToken: ct);
                }
                else
                {
                    await client.SendMessage(msg.Chat.Id, $"⚠️ Предупреждение {Warnings[msg.From.Id]}/{Config.MaxWarnings} за некорректные слова. При повторении — бан на 2.5 недели.", cancellationToken: ct);
                }
                return;
            }
            if (!Config.AdminChatIds.Contains(msg.From.Id) && BanUntil.TryGetValue(msg.From.Id, out var until))
            {
                if (DateTime.Now < until)
                {
                    await client.SendMessage(msg.Chat.Id, $"⛔ Вы забанены в боте до {until:dd.MM.yyyy HH:mm}.", cancellationToken: ct);
                    return;
                }
                BanUntil.Remove(msg.From.Id);
            }

            // если пользователь пытается прислать фото вместо текста
            if (WaitingTicket.Contains(msg.Chat.Id) && msg.Text is null)
            {
                await client.SendMessage(msg.Chat.Id, "📷 Фото пока не принимаются. Опишите проблему текстом.", cancellationToken: ct);
                return;
            }

            // если админ отвечает пользователю — диалог продолжается, пока не напишет /стоп
            if (ReplyToUser.ContainsKey(msg.Chat.Id) && !text.StartsWith('/'))
            {
                var (targetUserId, _) = ReplyToUser[msg.Chat.Id];
                try
                {
                    await client.SendMessage(targetUserId, $"✉️ Ответ от поддержки:\n{text}", cancellationToken: ct);
                    await client.SendMessage(msg.Chat.Id, $"✅ Отправлено игроку. /стоп — выйти.", cancellationToken: ct);
                }
                catch
                {
                    await client.SendMessage(msg.Chat.Id, "❌ Не удалось отправить — пользователь не начал диалог с ботом.", cancellationToken: ct);
                }
                return;
            }

            // игрок пишет в тикете — пересылаем админам
            if (TicketChat.Contains(msg.Chat.Id) && !text.StartsWith('/') && !Config.AdminChatIds.Contains(msg.From.Id))
            {
                foreach (var adminId in Config.AdminChatIds)
                {
                    await client.SendMessage(adminId,
                        $"🎫 Тикет от {msg.From.FirstName}:\n{text}",
                        cancellationToken: ct);
                }
                await client.SendMessage(msg.Chat.Id, "✅ Сообщение отправлено поддержке.", cancellationToken: ct);

                // ИИ-подсказка для админов
                var aiAnswer = await AskAi(text);
                if (!string.IsNullOrWhiteSpace(aiAnswer))
                {
                    PendingAiAnswer[msg.Chat.Id] = aiAnswer;
                    foreach (var adminId in Config.AdminChatIds)
                    {
                        await client.SendMessage(adminId,
                            $"💡 ИИ предлагает ответ:\n{aiAnswer}",
                            replyMarkup: new InlineKeyboardMarkup(new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📤 Отправить игроку", $"sendai_{msg.Chat.Id}"),
                            }),
                            cancellationToken: ct);
                    }
                }
                return;
            }

            // если пользователь пишет тикет — проверяем форму
            var isCommandButton = text is "Тикет" or "Донат" or "Правила" or "Новости" or "Админка" or "Ссылки" or "Инфо" or "Помощь";
            if (WaitingTicket.Contains(msg.Chat.Id) && !text.StartsWith('/') && !isCommandButton)
            {
                WaitingTicket.Remove(msg.Chat.Id);

                // форма: никнейм / причина / текст жалобы — минимум 3 строки
                var formLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (formLines.Length < 3)
                {
                    // формат одной строкой: "1. ник 2. причина 3. жалоба"
                    var numbered = System.Text.RegularExpressions.Regex.Split(text, @"\s+[123](?:\.|\s)\s*")
                        .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    if (numbered.Count >= 3)
                    {
                        text = string.Join("\n", numbered);
                        formLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    }
                }
                if (formLines.Length < 3)
                {
                    FormViolations[msg.Chat.Id] = FormViolations.GetValueOrDefault(msg.Chat.Id) + 1;
                    await client.SendMessage(msg.Chat.Id,
                        $"⚠️ Замечание {FormViolations[msg.Chat.Id]}: тикет подан не по форме.\n\nПишите СТРОГО по форме:\n1. Ваш игровой никнейм\n2. Причина жалобы/претензии\n3. Сама жалоба (грамотно)",
                        cancellationToken: ct);
                    return;
                }

                ChoicePending.Add(msg.Chat.Id);
                PendingTicketText[msg.Chat.Id] = text;

                await client.SendMessage(msg.Chat.Id,
                    "Как удобнее общаться?",
                    replyMarkup: new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("💬 Личка с поддержкой", $"chat_{msg.Chat.Id}"),
                            InlineKeyboardButton.WithCallbackData("🎫 Продолжить в тикет", $"ticket_{msg.Chat.Id}"),
                        }
                    }),
                    cancellationToken: ct);

                foreach (var adminId in Config.AdminChatIds)
                {
                    await client.SendMessage(adminId,
                        $"ℹ️ {msg.From.FirstName} предложен выбор: личка или тикет. Ожидает ответа пользователя.",
                        cancellationToken: ct);
                }
                return;
            }

            // живой чат с поддержкой
            if (SupportChat.Contains(msg.Chat.Id) && !text.StartsWith('/') && !Config.AdminChatIds.Contains(msg.From.Id))
            {
                if (PausedChats.Contains(msg.Chat.Id))
                {
                    await client.SendMessage(msg.Chat.Id, "⏳ Диалог на паузе. Ожидайте, администрация скоро ответит.", cancellationToken: ct);
                    return;
                }
                foreach (var adminId in Config.AdminChatIds)
                {
                    await client.SendMessage(adminId,
                        $"💬 Личка от {msg.From.FirstName}:\n{text}",
                        cancellationToken: ct);
                }
                await client.SendMessage(msg.Chat.Id, "✅ Сообщение отправлено поддержке.", cancellationToken: ct);

                // ИИ-подсказка для админов
                var aiAnswer = await AskAi(text);
                if (!string.IsNullOrWhiteSpace(aiAnswer))
                {
                    PendingAiAnswer[msg.Chat.Id] = aiAnswer;
                    foreach (var adminId in Config.AdminChatIds)
                    {
                        await client.SendMessage(adminId,
                            $"💡 ИИ предлагает ответ:\n{aiAnswer}",
                            replyMarkup: new InlineKeyboardMarkup(new[]
                            {
                                InlineKeyboardButton.WithCallbackData("📤 Отправить игроку", $"sendai_{msg.Chat.Id}"),
                            }),
                            cancellationToken: ct);
                    }
                }
                return;
            }

            switch (text.Split(' ')[0])
            {
                case "/start":
                    await client.SendMessage(msg.Chat.Id,
                        "👋 Привет, я бот LokitMine!\n\nВыбери действие:",
                        replyMarkup: MainMenu(Config.AdminChatIds.Contains(msg.From.Id)),
                        cancellationToken: ct);
                    break;

                case "/донат":
                case "Донат":
                    var lines = Config.Donations.Select((d, i) =>
                    {
                        var label = d.Price == 0 ? $"• {d.Name} — Бесплатно" : $"• {d.Name} — {d.Price}₽";
                        return $"[{label}](callback://donat_{i})";
                    });
                    await client.SendMessage(msg.Chat.Id,
                        "💰 Привилегии на сервере:\n" + string.Join("\n", lines) +
                        "\n\nНажмите на привилегию, чтобы узнать её плюсы.\n\nДля покупки: переведите нужную сумму на карту и напишите админу.\n💳 Сбербанк: 9112 3880 3789 4180\n👤 Елена Харисова",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: ct);
                    break;

                case "/правила":
                case "Правила":
                    await client.SendMessage(msg.Chat.Id,
                        "📜 Правила сервера:\n1. Уважайте друг друга\n2. Без мата\n3. Без гриферства\n4. Реклама — только с разрешения\n5. NSFW запрещён",
                        cancellationToken: ct);
                    break;

                case "/новости":
                case "Новости":
                    await client.SendMessage(msg.Chat.Id, "📰 Новостей пока нет. Загляди позже!", cancellationToken: ct);
                    break;

                case "/монитор":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        if (SupportChat.Count == 0)
                        {
                            await client.SendMessage(msg.Chat.Id, "👥 Живых диалогов нет.", cancellationToken: ct);
                            break;
                        }
                        var chats = SupportChat.Select(id => $"💬 id `{id}`{(PausedChats.Contains(id) ? " (пауза)" : "")}");
                        var kb = new InlineKeyboardMarkup(SupportChat.Select(id =>
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData($"⏸ Пауза {id}", $"pause_{id}"),
                                InlineKeyboardButton.WithCallbackData($"▶️ Продолжить {id}", $"resume_{id}"),
                            }));
                        await client.SendMessage(msg.Chat.Id,
                            "👥 Живые диалоги:\n" + string.Join("\n", chats),
                            replyMarkup: kb,
                            cancellationToken: ct);
                    }
                    break;

                case "/предупредить":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        var args = text.Split(' ', 3);
                        if (args.Length == 3 && long.TryParse(args[1], out var warnId))
                        {
                            await client.SendMessage(warnId, $"⚠️ Предупреждение от поддержки:\n{args[2]}", cancellationToken: ct);
                            await client.SendMessage(msg.Chat.Id, "✅ Предупреждение отправлено.", cancellationToken: ct);
                        }
                        else
                        {
                            await client.SendMessage(msg.Chat.Id, "Использование: /предупредить <id> <текст>", cancellationToken: ct);
                        }
                    }
                    break;

                case "/бан":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        var args = text.Split(' ', 4);
                        if (args.Length >= 3 && long.TryParse(args[1], out var banId))
                        {
                            var reasonCode = args[2].Trim();
                            var reason = Config.BanReasons.FirstOrDefault(r => r.Code == reasonCode || r.Reason == reasonCode);
                            if (reason == default)
                            {
                                await client.SendMessage(msg.Chat.Id,
                                    "Укажите причину:\n1.1 — не по назначению темы (7 дней)\n1.2 — буллинг (2 недели)\n1.3 — неккоректные слова (3 недели)\n\nКуда банить: тикеты или бот (по умолчанию бот)\nПример: /бан 123456 1.2 тикеты",
                                    cancellationToken: ct);
                                break;
                            }
                            var scope = args.Length >= 4 ? args[3].Trim().ToLower() : "бот";
                            if (scope == "тикеты")
                            {
                                TicketBanUntil[banId] = DateTime.Now.AddDays(reason.Days);
                                BanHistory.Add((banId, $"{reason.Code} {reason.Reason} (тикеты)", DateTime.Now, null));
                                try
                                {
                                    await client.SendMessage(banId, $"⛔ Вы забанены в тикетах на {reason.Days} дней.\nПричина: {reason.Code} — {reason.Reason}.", cancellationToken: ct);
                                }
                                catch { }
                                await client.SendMessage(msg.Chat.Id, $"✅ id {banId} забанен в тикетах на {reason.Days} дней. Причина: {reason.Code} — {reason.Reason}.", cancellationToken: ct);
                            }
                            else
                            {
                                BanUntil[banId] = DateTime.Now.AddDays(reason.Days);
                                BanHistory.Add((banId, $"{reason.Code} {reason.Reason} (бот)", DateTime.Now, null));
                                try
                                {
                                    await client.SendMessage(banId, $"⛔ Вы забанены в боте на {reason.Days} дней.\nПричина: {reason.Code} — {reason.Reason}.", cancellationToken: ct);
                                }
                                catch { }
                                await client.SendMessage(msg.Chat.Id, $"✅ id {banId} забанен в боте на {reason.Days} дней. Причина: {reason.Code} — {reason.Reason}.", cancellationToken: ct);
                            }
                        }
                        else
                        {
                            await client.SendMessage(msg.Chat.Id, "Использование: /бан <id> <1.1|1.2|1.3> [тикеты|бот]", cancellationToken: ct);
                        }
                    }
                    break;

                case "/разбан":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        var args = text.Split(' ');
                        if (args.Length == 2 && long.TryParse(args[1], out var unbanId))
                        {
                            BanUntil.Remove(unbanId);
                            TicketBanUntil.Remove(unbanId);
                            var last = BanHistory.LastOrDefault(h => h.Id == unbanId && h.Unbanned is null);
                            if (last != default)
                            {
                                BanHistory.Remove(last);
                                BanHistory.Add((last.Id, last.Reason, last.Banned, DateTime.Now));
                            }
                            try
                            {
                                await client.SendMessage(unbanId, "✅ Вы разбанены в боте.", cancellationToken: ct);
                            }
                            catch { }
                            await client.SendMessage(msg.Chat.Id, $"✅ id {unbanId} разбанен.", cancellationToken: ct);
                        }
                        else
                        {
                            await client.SendMessage(msg.Chat.Id, "Использование: /разбан <id>", cancellationToken: ct);
                        }
                    }
                    break;

                case "/банлист":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        var active = BanUntil.Where(kv => kv.Value > DateTime.Now).ToList();
                        if (active.Count == 0)
                        {
                            await client.SendMessage(msg.Chat.Id, "📋 Активных банов нет.", cancellationToken: ct);
                            break;
                        }
                        var list = active.Select(kv => $"• id {kv.Key} — бот — до {kv.Value:dd.MM.yyyy HH:mm}");
                        var ticketBans = TicketBanUntil.Where(kv => kv.Value > DateTime.Now).ToList();
                        if (ticketBans.Count > 0)
                        {
                            list = list.Concat(ticketBans.Select(kv => $"• id {kv.Key} — тикеты — до {kv.Value:dd.MM.yyyy HH:mm}")).ToList();
                        }
                        await client.SendMessage(msg.Chat.Id, "📋 Активные баны:\n" + string.Join("\n", list), cancellationToken: ct);
                    }
                    break;

                case "/историябан":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        if (BanHistory.Count == 0)
                        {
                            await client.SendMessage(msg.Chat.Id, "📋 История банов пуста.", cancellationToken: ct);
                            break;
                        }
                        var list = BanHistory.Select(h => $"• id {h.Id} — {h.Reason} ({h.Banned:dd.MM HH:mm}){(h.Unbanned is null ? "" : $" — разбанен {h.Unbanned:dd.MM HH:mm}")}");
                        await client.SendMessage(msg.Chat.Id, "📋 История банов:\n" + string.Join("\n", list), cancellationToken: ct);
                    }
                    break;

                case "/замечания":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        if (FormViolations.Count == 0)
                        {
                            await client.SendMessage(msg.Chat.Id, "📋 Замечаний нет.", cancellationToken: ct);
                            break;
                        }
                        var list = FormViolations.Select(kv => $"• id {kv.Key} — замечаний: {kv.Value}");
                        await client.SendMessage(msg.Chat.Id, "📋 Замечания за форму:\n" + string.Join("\n", list), cancellationToken: ct);
                    }
                    break;

                case "/стоп":
                    SupportChat.Remove(msg.Chat.Id);
                    TicketChat.Remove(msg.Chat.Id);
                    if (ReplyToUser.ContainsKey(msg.Chat.Id))
                    {
                        ReplyToUser.Remove(msg.Chat.Id);
                        await client.SendMessage(msg.Chat.Id, "👋 Вы вышли из диалога с игроком.", cancellationToken: ct);
                    }
                    else
                    {
                        await client.SendMessage(msg.Chat.Id, "👋 Вы вышли из живого чата. Для новой помощи — /тикет.", cancellationToken: ct);
                    }
                    break;

                case "/тикет":
                case "Тикет":
                    if (TicketBanUntil.TryGetValue(msg.From.Id, out var tb) && DateTime.Now < tb)
                    {
                        await client.SendMessage(msg.Chat.Id, $"⛔ Вы забанены в тикетах до {tb:dd.MM.yyyy HH:mm}.", cancellationToken: ct);
                        break;
                    }
                    WaitingTicket.Add(msg.Chat.Id);
                    await client.SendMessage(msg.Chat.Id, "✍️ Писать тикет СТРОГО ПО ФОРМЕ:\n\nВаш игровой никнейм\nПричина жалобы/претензии\nСама жалоба (Граммотно, непонятные не принимаем)\n\nЗа помощью можно писать сюда в боте или на Discord-сервере LokitMine.", cancellationToken: ct);
                    break;

                case "/админ":
                case "Админка":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                        await client.SendMessage(msg.Chat.Id,
                            "🛡 Админ-панель\n/тикеты — список тикетов\n/бан <id> <1.1|1.2|1.3> — бан\n/разбан <id> — разбанить\n/банлист — активные баны\n/историябан — история\n/замечания — форма тикетов\n/монитор — живые диалоги\n/админ — эта панель",
                            cancellationToken: ct);
                    break;

                case "/тикеты":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        if (Tickets.Count == 0)
                        {
                            await client.SendMessage(msg.Chat.Id, "🎫 Тикетов нет.", cancellationToken: ct);
                            break;
                        }
                        var ticketList = Tickets.Select((t, i) => $"🎫 #{i + 1} — {t.Name}\n{t.Text}\n🕐 {t.Date:HH:mm}");
                        var kb = new InlineKeyboardMarkup(Tickets.Select((t, i) =>
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData($"✉️ Ответить #{i + 1}", $"reply_{t.ChatId}"),
                                InlineKeyboardButton.WithCallbackData($"❌ Отказать #{i + 1}", $"refuse_{t.ChatId}"),
                                InlineKeyboardButton.WithCallbackData($"🔒 Закрыть #{i + 1}", $"close_{t.ChatId}"),
                            }));
                        await client.SendMessage(msg.Chat.Id,
                            string.Join("\n\n", ticketList),
                            replyMarkup: kb,
                            cancellationToken: ct);
                    }
                    break;

                case "/тикетов":
                    if (Config.AdminChatIds.Contains(msg.From.Id))
                    {
                        WaitingTicket.Clear();
                        await client.SendMessage(msg.Chat.Id, "✅ Счётчик тикетов сброшен.", cancellationToken: ct);
                    }
                    break;

                case "/id":
                    await client.SendMessage(msg.Chat.Id, $"🆔 Ваш ID: {msg.From.Id}", cancellationToken: ct);
                    break;

                case "/инфо":
                case "Инфо":
                    await client.SendMessage(msg.Chat.Id,
                        "📜 Правила сервера:\nПравила ещё не придуманы — скоро появятся!\n\n🌐 Наши площадки:\n💬 Discord: " + Config.DiscordInvite + "\n✈️ Telegram: " + Config.TelegramChannel + "\n💙 VK: " + Config.VkLink,
                        cancellationToken: ct);
                    break;

                case "/ссылки":
                case "/ссылка":
                case "/соцсети":
                case "Ссылки":
                    await client.SendMessage(msg.Chat.Id,
                        $"🌐 Наши площадки:\n💬 Discord: {Config.DiscordInvite}\n✈️ Telegram: {Config.TelegramChannel}\n💙 VK: {Config.VkLink}",
                        cancellationToken: ct);
                    break;

                case "/контакты":
                case "/админы":
                    var adminLines = Config.Admins.Select(a => $"• {a.Name}: {a.Username} — {a.Role}");
                    await client.SendMessage(msg.Chat.Id,
                        "👥 Администрация:\n" + string.Join("\n", adminLines),
                        cancellationToken: ct);
                    break;

                case "/карта":
                    await client.SendMessage(msg.Chat.Id,
                        $"💳 Реквизиты для оплаты:\n🏦 {Config.CardBank}\n🔢 {Config.CardNumber}\n👤 {Config.CardHolder}",
                        cancellationToken: ct);
                    break;

                case "/помощь":
                case "/help":
                case "Помощь":
                    await client.SendMessage(msg.Chat.Id,
                        "📖 Команды бота:\n/start — меню\n/донат — привилегии\n/тикет — поддержка\n/инфо — правила и ссылки\n/помощь — это сообщение\n\nТикет пишите СТРОГО по форме:\n1. Ваш игровой никнейм\n2. Причина жалобы/претензии\n3. Сама жалоба (грамотно)\n\nПравильный пример (можно одной строкой):\n1 lokitmine 2 Нашёл баг 3 Баг с дюпом алмазов, приложил скрин",
                        cancellationToken: ct);
                    break;

                default:
                    if (text.StartsWith('/'))
                        await client.SendMessage(msg.Chat.Id, "Неизвестная команда. Напишите /start", cancellationToken: ct);
                    break;
            }
        }

        static ReplyKeyboardMarkup MainMenu(bool isAdmin) => isAdmin
            ? new(new[]
            {
                new KeyboardButton[] { "Донат", "Тикет" },
                new KeyboardButton[] { "Новости", "Инфо" },
                new KeyboardButton[] { "Помощь", "Админка" },
            })
            { ResizeKeyboard = true }
            : new(new[]
            {
                new KeyboardButton[] { "Донат", "Тикет" },
                new KeyboardButton[] { "Новости", "Инфо" },
                new KeyboardButton[] { "Помощь" },
            })
            { ResizeKeyboard = true };

        static Task HandleError(ITelegramBotClient client, Exception exception, CancellationToken ct)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}
