using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using SpotifyExplode;
using SpotifyExplode.Search;
using Telegram.Bot.Types.ReplyMarkups;
using SpotifyExplode.Artists;
using OpenQA.Selenium.DevTools.V127.Tracing;
using SpotifyExplode.Tracks;
using SpotifyExplode.Albums;
using System.Text;

namespace MusicSave
{
    class Program
    {
        private static ITelegramBotClient soundCloudDownloadBot;
        private static ReceiverOptions _receiverOptions;
        private static SpotifyClient spotify;
        private static readonly HttpClient _httpClient;
        private static AlbumSearch _albumSearch;
        private static string lastAction = null;

        static Program()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(30) // Увеличиваем таймаут до 30 минут
            };

            // Добавляем базовые заголовки
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        static async Task Main(string[] args)
        {
            try
            {
                var botToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
                if (string.IsNullOrEmpty(botToken))
                {
                    throw new Exception("BOT_TOKEN environment variable is not set!");
                }

                Console.WriteLine("Starting bot...");

                spotify = new SpotifyClient();
                soundCloudDownloadBot = new TelegramBotClient(botToken);
                _albumSearch = new AlbumSearch(spotify, soundCloudDownloadBot);
                _receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    ThrowPendingUpdates = true,
                };

                using var cts = new CancellationTokenSource();
                soundCloudDownloadBot.StartReceiving(HandleUpdateAsync, ErrorHandler, _receiverOptions, cts.Token);

                var me = await soundCloudDownloadBot.GetMeAsync();
                Console.WriteLine($"{me.FirstName} запущен!");

                await Task.Delay(-1); // Устанавливаем бесконечную задержку
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                throw;
            }
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"Received update type: {update.Type}");

                switch (update.Type)
                {
                    case UpdateType.Message:
                        {
                            var message = update.Message;
                            var chat = message.Chat;

                            switch (message.Type)
                            {
                                case MessageType.Text:
                                    if (message.Text.StartsWith("/"))
                                    {
                                        var keyboard = new InlineKeyboardMarkup(new[]
                                        {
                                            new []
                                            {
                                                InlineKeyboardButton.WithCallbackData("Поиск исполнителя", "search_artist"),
                                                InlineKeyboardButton.WithCallbackData("Поиск альбома", "search_album")
                                            }
                                        });

                                        await botClient.SendTextMessageAsync(chat.Id, "Выберите тип поиска:", replyMarkup: keyboard);
                                        return;
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Получен текст: {message.Text}");
                                        Console.WriteLine($"lastAction: {lastAction}");

                                        if (lastAction == "search_album")
                                        {
                                            Console.WriteLine("Начинаем поиск альбома...");
                                            await _albumSearch.SearchAlbumAndCreateButtons(chat.Id, message.Text);
                                            return;
                                        }
                                        else
                                        {
                                            Console.WriteLine("Начинаем поиск треков исполнителя...");
                                            await SearchArtistTracksAndCreateButtons(chat.Id, message.Text);
                                            return;
                                        }
                                    }
                                case MessageType.Audio:
                                case MessageType.Voice:
                                case MessageType.Document:
                                    await botClient.SendTextMessageAsync(
                                        chat.Id,
                                        "Пожалуйста, используйте поиск или команды бота для добавления музыки."
                                    );
                                    break;
                                default:
                                    await botClient.SendTextMessageAsync(
                                        chat.Id,
                                        "Используйте только текст для поиска музыки!"
                                    );
                                    break;
                            }
                            break;
                        }
                    case UpdateType.CallbackQuery:
                        {
                            var callbackQuery = update.CallbackQuery;
                            var chat = callbackQuery.Message.Chat;

                            switch (callbackQuery.Data)
                            {
                                case "search_artist":
                                    {
                                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                                        lastAction = null; // Сбрасываем флаг поиска альбома
                                        await botClient.SendTextMessageAsync(
                                            chat.Id,
                                            "Введите имя исполнителя:");
                                        return;
                                    }

                                case "search_album":
                                    {
                                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
                                        lastAction = "search_album"; // Устанавливаем флаг поиска альбома
                                        await botClient.SendTextMessageAsync(
                                            chat.Id,
                                            "Введите название альбома:");
                                        return;
                                    }

                                default:
                                    {
                                        if (callbackQuery.Data.StartsWith("track_"))
                                        {
                                            var trackId = callbackQuery.Data.Split('_')[1];

                                            var buttonId = callbackQuery.Id;
                                            // Получаем трек по ID
                                            var track = await spotify.Tracks.GetAsync(trackId);

                                            // Проверяем, что трек найден
                                            if (track == null || string.IsNullOrEmpty(track.Url))
                                            {
                                                await botClient.SendTextMessageAsync(chat.Id, "Трек не найден.");
                                                return;
                                            }

                                            var trackUrl = track.Url;
                                            Console.WriteLine(trackUrl);

                                            try
                                            {
                                                // Получаем ссылку для скачивания трека
                                                var downloadUrl = await spotify.Tracks.GetDownloadUrlAsync(trackUrl);

                                                // Отправляем файл
                                                await SendTrackByStreamAsync(botClient, chat.Id, downloadUrl, "Вот ваш трек!");
                                            }
                                            catch (Exception ex)
                                            {
                                                await botClient.SendTextMessageAsync(chat.Id,
                                                    $"Произошла ошибка при скачивании трека: {ex.Message}");
                                                Console.WriteLine($"Error downloading track: {ex}");
                                            }
                                        }
                                        else if (callbackQuery.Data.StartsWith("page_"))
                                        {
                                            var parts = callbackQuery.Data.Split('_');
                                            if (parts.Length >= 3)
                                            {
                                                var page = int.Parse(parts[1]);
                                                var artistName = string.Join("_", parts.Skip(2));
                                                await SearchArtistTracksAndCreateButtons(chat.Id, artistName, page, callbackQuery.Message.MessageId);
                                            }
                                        }
                                        else if (callbackQuery.Data.StartsWith("album_"))
                                        {
                                            var albumId = callbackQuery.Data.Split('_')[1];
                                            await _albumSearch.ShowAlbumTracks(chat.Id, albumId);
                                        }
                                        else if (callbackQuery.Data.StartsWith("albumpage_"))
                                        {
                                            var parts = callbackQuery.Data.Split('_');
                                            if (parts.Length >= 3)
                                            {
                                                var page = int.Parse(parts[1]);
                                                var albumName = string.Join("_", parts.Skip(2));
                                                await _albumSearch.SearchAlbumAndCreateButtons(chat.Id, albumName, page, callbackQuery.Message.MessageId);
                                            }
                                        }
                                        break;
                                    }
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                await botClient.SendTextMessageAsync(update.Message.Chat.Id,
                    "Произошла ошибка. Пожалуйста, попробуйте позже.");
            }
        }

        private static async Task SearchArtistTracksAndCreateButtons(long chatId, string artistName, int page = 0, int? messageId = null)
        {
            try
            {
                Console.WriteLine($"Поиск треков исполнителя: {artistName}, страница: {page}");
                string searchQuery = $"artist:\"{artistName?.Trim()}\"";
                var searchResults = await spotify.Search.GetResultsAsync(searchQuery);

                if (searchResults == null || !searchResults.Any())
                {
                    await soundCloudDownloadBot.SendTextMessageAsync(chatId, $"Не удалось найти треки исполнителя '{artistName}'. Попробуйте уточнить имя.");
                    return;
                }

                var allTracks = new List<TrackSearchResult>();
                string artistImageUrl = null;
                string firstArtistId = null;

                foreach (var result in searchResults)
                {
                    if (result is TrackSearchResult track)
                    {
                        allTracks.Add(track);
                        // Сохраняем ID первого найденного исполнителя для получения его фото
                        if (firstArtistId == null && track.Artists.Any())
                        {
                            firstArtistId = track.Artists.First().Id;
                        }
                    }
                }

                if (allTracks.Count == 0)
                {
                    await soundCloudDownloadBot.SendTextMessageAsync(chatId, $"Не найдено треков исполнителя '{artistName}'.");
                    return;
                }

                // Получаем информацию об исполнителе, включая фото
                if (firstArtistId != null)
                {
                    var artist = await spotify.Artists.GetAsync(firstArtistId);
                    if (artist?.Images != null && artist.Images.Any())
                    {
                        artistImageUrl = artist.Images.OrderByDescending(i => i.Height).FirstOrDefault()?.Url;
                    }
                }

                // Получаем треки для текущей страницы
                var tracksPerPage = 10;
                var skip = page * tracksPerPage;
                var currentPageTracks = allTracks.Skip(skip).Take(tracksPerPage).ToList();

                // Создаем кнопки для треков
                var buttons = new List<InlineKeyboardButton[]>();
                foreach (var track in currentPageTracks)
                {
                    var buttonText = $"{string.Join(", ", track.Artists.Select(a => a.Name))} - {track.Title}";
                    if (buttonText.Length > 64)
                    {
                        buttonText = buttonText.Substring(0, 61) + "...";
                    }
                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(buttonText, $"track_{track.Id}")
                    });
                }

                // Добавляем кнопки навигации
                var navigationButtons = new List<InlineKeyboardButton>();
                if (page > 0)
                {
                    navigationButtons.Add(InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"page_{page - 1}_{artistName}"));
                }

                if (skip + tracksPerPage < allTracks.Count)
                {
                    navigationButtons.Add(InlineKeyboardButton.WithCallbackData("Вперед ➡️", $"page_{page + 1}_{artistName}"));
                }

                if (navigationButtons.Any())
                {
                    buttons.Add(navigationButtons.ToArray());
                }

                var inlineKeyboard = new InlineKeyboardMarkup(buttons);
                var messageText = $"👤 Исполнитель: {artistName}\n📀 Найдено треков: {allTracks.Count}\n\nВыберите трек для скачивания:";

                if (messageId.HasValue)
                {
                    if (!string.IsNullOrEmpty(artistImageUrl))
                    {
                        // Удаляем старое сообщение
                        await soundCloudDownloadBot.DeleteMessageAsync(chatId, messageId.Value);
                        // Отправляем новое с фото
                        var inputFile = new InputFileUrl(artistImageUrl);
                        await soundCloudDownloadBot.SendPhotoAsync(
                            chatId: chatId,
                            photo: inputFile,
                            caption: messageText,
                            replyMarkup: inlineKeyboard
                        );
                    }
                    else
                    {
                        await soundCloudDownloadBot.EditMessageTextAsync(
                            chatId,
                            messageId.Value,
                            messageText,
                            replyMarkup: inlineKeyboard
                        );
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(artistImageUrl))
                    {
                        Console.WriteLine($"Отправляем сообщение с фото исполнителя: {artistImageUrl}");
                        var inputFile = new InputFileUrl(artistImageUrl);
                        await soundCloudDownloadBot.SendPhotoAsync(
                            chatId: chatId,
                            photo: inputFile,
                            caption: messageText,
                            replyMarkup: inlineKeyboard
                        );
                    }
                    else
                    {
                        Console.WriteLine("Отправляем сообщение без фото");
                        await soundCloudDownloadBot.SendTextMessageAsync(
                            chatId: chatId,
                            text: messageText,
                            replyMarkup: inlineKeyboard
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске треков: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await soundCloudDownloadBot.SendTextMessageAsync(chatId,
                    "Произошла ошибка при поиске треков. Пожалуйста, попробуйте позже или уточните запрос.");
            }
        }

        private static async Task SendTrackByStreamAsync(ITelegramBotClient botClient, long chatId, string url, string caption)
        {
            Stream trackStream = null;
            try
            {
                Console.WriteLine($"Processing URL: {url}");

                if (url.Contains("spotify.com"))
                {
                    var trackId = url.Split('/').Last();
                    Console.WriteLine($"Getting download URL for Spotify track ID: {trackId}");
                    url = await spotify.Tracks.GetDownloadUrlAsync(trackId);

                    if (string.IsNullOrEmpty(url))
                    {
                        throw new Exception("Не удалось получить ссылку на скачивание");
                    }
                    Console.WriteLine($"Got download URL: {url}");
                }

                trackStream = await DownloadTrackAsStreamAsync(url);

                var fileName = $"track_{DateTime.Now:yyyyMMddHHmmss}.mp3";
                Console.WriteLine($"Sending audio file: {fileName}");

                var message = await botClient.SendAudioAsync(
                    chatId: chatId,
                    audio: InputFile.FromStream(trackStream, fileName),
                    caption: caption);

                Console.WriteLine("Audio sent successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SendTrackByStreamAsync: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Ошибка при получении трека: {ex.Message}\nПопробуйте еще раз или выберите другой трек.");
            }
            finally
            {
                if (trackStream != null)
                {
                    await trackStream.DisposeAsync();
                }
            }
        }

        private static async Task<Stream> DownloadTrackAsStreamAsync(string url)
        {
            try
            {
                Console.WriteLine($"Starting download from: {url}");

                HttpResponseMessage response;
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                }

                Console.WriteLine($"Initial response status: {response.StatusCode}");

                // Следуем по редиректам вручную
                int maxRedirects = 10;
                int redirectCount = 0;

                while ((response.StatusCode == System.Net.HttpStatusCode.Found ||
                       response.StatusCode == System.Net.HttpStatusCode.Moved ||
                       response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                       response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect ||
                       response.StatusCode == System.Net.HttpStatusCode.PermanentRedirect) &&
                       redirectCount < maxRedirects)
                {
                    var redirectUrl = response.Headers.Location;
                    if (redirectUrl == null)
                        break;

                    // Если URL относительный, делаем его абсолютным
                    if (!redirectUrl.IsAbsoluteUri)
                    {
                        redirectUrl = new Uri(new Uri(url), redirectUrl);
                    }

                    Console.WriteLine($"Following redirect #{redirectCount + 1} to: {redirectUrl}");

                    using (var request = new HttpRequestMessage(HttpMethod.Get, redirectUrl))
                    {
                        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    }
                    Console.WriteLine($"Redirect response status: {response.StatusCode}");

                    redirectCount++;
                }

                if (redirectCount >= maxRedirects)
                {
                    throw new Exception("Too many redirects");
                }

                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength ?? -1;
                Console.WriteLine($"Content length: {contentLength} bytes");

                var stream = await response.Content.ReadAsStreamAsync();
                var memoryStream = new MemoryStream();

                var buffer = new byte[81920]; // Увеличиваем размер буфера до 80KB
                var totalBytesRead = 0L;
                int bytesRead;
                var lastProgressReport = DateTime.Now;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    // Отображаем прогресс каждые 2 секунды
                    var now = DateTime.Now;
                    if ((now - lastProgressReport).TotalSeconds >= 2)
                    {
                        if (contentLength > 0)
                        {
                            var progress = (double)totalBytesRead / contentLength * 100;
                            Console.WriteLine($"Download progress: {progress:F1}% ({totalBytesRead}/{contentLength} bytes)");
                        }
                        else
                        {
                            Console.WriteLine($"Downloaded: {totalBytesRead} bytes");
                        }
                        lastProgressReport = now;
                    }
                }

                Console.WriteLine("Download completed successfully");
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading track: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static Task ErrorHandler(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
    }
}
