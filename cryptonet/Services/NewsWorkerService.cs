using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using cryptonet.Data;
using cryptonet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;

namespace cryptonet.Workers
{
    public class NewsWorkerService : BackgroundService
    {
        private readonly DbConnectionFactory _db;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly List<string> _rssUrls = new List<string>
        {
            "https://cointelegraph.com/rss/tag/bitcoin",
            "https://cointelegraph.com/rss/tag/ethereum"
        };

        public NewsWorkerService(DbConnectionFactory db, IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Console.WriteLine($"[Worker] Запуск цикла обновления новостей: {DateTime.Now}");
                await FetchRssNews();

                // Интервал обновления — например, каждые 15 минут (можно поменять по необходимости)
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }

        private async Task FetchRssNews()
        {
            try
            {
                using var connection = (MySqlConnection)_db.CreateConnection();
                connection.Open();

                // 1. Парсинг RSS-лент и перевод каждой отдельной новости
                foreach (var url in _rssUrls)
                {
                    try
                    {
                        using var xmlReader = XmlReader.Create(url);
                        var feed = SyndicationFeed.Load(xmlReader);

                        foreach (var item in feed.Items.Take(5))
                        {
                            var link = item.Links.FirstOrDefault()?.Uri.ToString();

                            // Проверяем, нет ли уже этой новости в базе по URL
                            var checkCmd = new MySqlCommand("SELECT COUNT(*) FROM news WHERE url = @url", connection);
                            checkCmd.Parameters.AddWithValue("@url", link);
                            if (Convert.ToInt32(checkCmd.ExecuteScalar()) > 0) continue;

                            // Создаем область видимости (scope) для получения GroqService
                            using (var scope = _scopeFactory.CreateScope())
                            {
                                var groq = scope.ServiceProvider.GetRequiredService<GroqService>();
                                var rawSummary = item.Summary?.Text ?? item.Title.Text;

                                // Отправляем в ИИ на перевод и анализ настроения
                                var (ruTitle, ruSummary, sentiment) = await groq.TranslateAndAnalyze(item.Title.Text, rawSummary);

                                // Записываем переведенную новость в таблицу
                                var insCmd = new MySqlCommand(@"
                                    INSERT INTO news (title, summary, source, url, sentiment, published_at)
                                    VALUES (@title, @summary, @source, @url, @sentiment, @publishedAt)", connection);

                                insCmd.Parameters.AddWithValue("@title", ruTitle);
                                insCmd.Parameters.AddWithValue("@summary", ruSummary);
                                insCmd.Parameters.AddWithValue("@source", new Uri(url).Host);
                                insCmd.Parameters.AddWithValue("@url", link);
                                insCmd.Parameters.AddWithValue("@sentiment", sentiment.Trim().ToLower());
                                insCmd.Parameters.AddWithValue("@publishedAt", item.PublishDate.DateTime);

                                insCmd.ExecuteNonQuery();
                                Console.WriteLine($"[Worker] Добавлена новость: {ruTitle}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Worker] Ошибка при обработке ленты {url}: {ex.Message}");
                    }
                }

                // 2. Формирование ГЛОБАЛЬНОГО вердикта ИИ по рынку
                Console.WriteLine("[Worker] Формирование общего вердикта по рынку...");
                var lastNews = new List<string>();

                // Достаем из базы последние 10 заголовков новостей, которые мы только что скачали или уже имели
                var getNewsCmd = new MySqlCommand("SELECT title FROM news ORDER BY published_at DESC LIMIT 10", connection);
                using (var reader = getNewsCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lastNews.Add(reader.GetString("title"));
                    }
                }

                // Если новости в базе есть — просим ИИ сделать общий анализ рынка
                if (lastNews.Count > 0)
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var groq = scope.ServiceProvider.GetRequiredService<GroqService>();
                        var (marketSentiment, marketSummary) = await groq.AnalyzeMarketState(lastNews);

                        // Записываем полученный вердикт в таблицу market_ai_analysis
                        var marketCmd = new MySqlCommand(@"
                            INSERT INTO market_ai_analysis (sentiment, summary, created_at) 
                            VALUES (@sentiment, @summary, NOW())", connection);

                        marketCmd.Parameters.AddWithValue("@sentiment", marketSentiment.Trim().ToLower());
                        marketCmd.Parameters.AddWithValue("@summary", marketSummary);

                        marketCmd.ExecuteNonQuery();
                        Console.WriteLine($"[Worker] Общий вердикт успешно обновлен: {marketSentiment}");
                    }
                }
                else
                {
                    Console.WriteLine("[Worker] Нет новостей в базе данных для проведения общего анализа.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Worker] Критическая ошибка воркера: {ex.Message}");
            }
        }
    }
}