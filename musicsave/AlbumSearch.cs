using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using SpotifyExplode;
using SpotifyExplode.Search;

namespace MusicSave
{
    public class AlbumSearch
    {
        private readonly SpotifyClient _spotify;
        private readonly ITelegramBotClient _botClient;

        public AlbumSearch(SpotifyClient spotify, ITelegramBotClient botClient)
        {
            _spotify = spotify;
            _botClient = botClient;
        }

        public async Task SearchAlbumAndCreateButtons(long chatId, string albumName, int page = 0, int? messageId = null)
        {
            try
            {
                Console.WriteLine($"Поиск альбома: {albumName}, страница: {page}");

                // Формируем поисковый запрос для альбома
                string searchQuery = $"album:\"{albumName?.Trim()}\"";
                Console.WriteLine($"Поисковый запрос: {searchQuery}");

                var searchResults = await _spotify.Search.GetResultsAsync(searchQuery);
                Console.WriteLine($"Получено результатов: {searchResults?.Count() ?? 0}");

                var uniqueAlbums = new Dictionary<string, (string albumName, string artistName, string albumId)>();

                foreach (var result in searchResults)
                {
                    Console.WriteLine($"Обработка результата типа: {result.GetType().Name}");
                    if (result is TrackSearchResult track && track.Album != null)
                    {
                        var albumId = track.Album.Id;
                        var artistName = string.Join(", ", track.Artists.Select(a => a.Name));
                        
                        if (!uniqueAlbums.ContainsKey(albumId))
                        {
                            Console.WriteLine($"Добавляем альбом: {track.Album.Name} от {artistName}");
                            uniqueAlbums[albumId] = (track.Album.Name, artistName, albumId);
                        }
                    }
                }

                var allAlbumResults = uniqueAlbums.Values.ToList();
                Console.WriteLine($"Всего уникальных альбомов: {allAlbumResults.Count}");

                if (allAlbumResults.Count == 0)
                {
                    await _botClient.SendTextMessageAsync(chatId,
                        $"Не найдено альбомов с названием '{albumName}'. Проверьте правильность написания.");
                    return;
                }

                // Получаем альбомы для текущей страницы
                var albumsPerPage = 10;
                var skip = page * albumsPerPage;
                var currentPageAlbums = allAlbumResults.Skip(skip).Take(albumsPerPage).ToList();
                Console.WriteLine($"Альбомов на текущей странице: {currentPageAlbums.Count}");

                // Создаем кнопки для альбомов
                var buttons = new List<InlineKeyboardButton[]>();
                foreach (var album in currentPageAlbums)
                {
                    var buttonText = $"{album.artistName} - {album.albumName}";
                    if (buttonText.Length > 64)
                    {
                        buttonText = buttonText.Substring(0, 61) + "...";
                    }
                    Console.WriteLine($"Создаем кнопку: {buttonText}");
                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData(buttonText, $"album_{album.albumId}")
                    });
                }

                // Добавляем кнопки навигации
                var navigationButtons = new List<InlineKeyboardButton>();

                if (page > 0)
                {
                    navigationButtons.Add(InlineKeyboardButton.WithCallbackData("⬅️ Назад", $"albumpage_{page - 1}_{albumName}"));
                }

                if (skip + albumsPerPage < allAlbumResults.Count)
                {
                    navigationButtons.Add(InlineKeyboardButton.WithCallbackData("Вперед ➡️", $"albumpage_{page + 1}_{albumName}"));
                }

                if (navigationButtons.Any())
                {
                    buttons.Add(navigationButtons.ToArray());
                }

                var inlineKeyboard = new InlineKeyboardMarkup(buttons);
                string messageText = $"Найдено {allAlbumResults.Count} альбомов по запросу '{albumName}' (страница {page + 1}):";

                Console.WriteLine($"Отправляем сообщение: {messageText}");
                Console.WriteLine($"Количество кнопок: {buttons.Count}");

                try
                {
                    if (messageId.HasValue)
                    {
                        Console.WriteLine($"Редактируем сообщение {messageId.Value}");
                        await _botClient.EditMessageTextAsync(
                            chatId,
                            messageId.Value,
                            messageText,
                            replyMarkup: inlineKeyboard
                        );
                    }
                    else
                    {
                        Console.WriteLine("Отправляем новое сообщение");
                        await _botClient.SendTextMessageAsync(
                            chatId,
                            messageText,
                            replyMarkup: inlineKeyboard
                        );
                    }
                    Console.WriteLine("Сообщение успешно отправлено");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при отправке сообщения: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске альбома: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await _botClient.SendTextMessageAsync(chatId,
                    "Произошла ошибка при поиске. Пожалуйста, попробуйте позже или уточните запрос.");
            }
        }

        public async Task ShowAlbumTracks(long chatId, string albumId)
        {
            try
            {
                Console.WriteLine($"Получаем треки альбома {albumId}");
                var album = await _spotify.Albums.GetAsync(albumId);
                var tracks = await _spotify.Albums.GetAllTracksAsync(albumId);

                if (tracks == null || !tracks.Any())
                {
                    await _botClient.SendTextMessageAsync(chatId, "В этом альбоме нет треков.");
                    return;
                }

                var buttons = new List<InlineKeyboardButton[]>();
                foreach (var track in tracks)
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

                var inlineKeyboard = new InlineKeyboardMarkup(buttons);
                var albumArtists = string.Join(", ", album.Artists.Select(a => a.Name));
                var messageText = $"🎵 Альбом: {album.Name}\n👤 Исполнитель: {albumArtists}\n\nВыберите трек для скачивания:";

                // Получаем URL обложки альбома
                var albumCover = album.Images.OrderByDescending(i => i.Height).FirstOrDefault()?.Url;
                
                if (!string.IsNullOrEmpty(albumCover))
                {
                    Console.WriteLine($"Отправляем сообщение с обложкой: {albumCover}");
                    var inputFile = new InputFileUrl(albumCover);
                    await _botClient.SendPhotoAsync(
                        chatId: chatId,
                        photo: inputFile,
                        caption: messageText,
                        replyMarkup: inlineKeyboard
                    );
                }
                else
                {
                    Console.WriteLine("Обложка не найдена, отправляем сообщение без обложки");
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: messageText,
                        replyMarkup: inlineKeyboard
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении треков альбома: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await _botClient.SendTextMessageAsync(chatId, 
                    "Произошла ошибка при получении треков альбома. Пожалуйста, попробуйте позже.");
            }
        }
    }
}
