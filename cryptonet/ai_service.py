from flask import Flask, request, jsonify
import numpy as np
from textblob import TextBlob # Не забудь: pip install textblob

app = Flask(__name__)

# 1. Анализ цен (для графиков и прогнозов)
@app.route("/analyze", methods=["POST"])
def analyze():
    data = request.json
    prices = data.get("prices", [])

    if len(prices) < 2:
        return jsonify({
            "trend": "neutral",
            "forecast": [],
            "risk": "low",
            "explanation": "Недостаточно данных"
        })

    prices = np.array(prices)
    trend = "bullish" if prices[-1] > prices[0] else "bearish"

    x = np.arange(len(prices))
    coeffs = np.polyfit(x, prices, 1)
    forecast = [(coeffs[0] * i + coeffs[1]) for i in range(len(prices), len(prices)+5)]

    volatility = np.std(prices)
    if volatility < 1:
        risk = "low"
    elif volatility < 5:
        risk = "medium"
    else:
        risk = "high"

    explanation = "Рост цены" if trend == "bullish" else "Падение цены"

    return jsonify({
        "trend": trend,
        "forecast": forecast,
        "risk": risk,
        "explanation": explanation
    })

# 2. АНАЛИЗ НОВОСТЕЙ (Для страницы новостей)
@app.route("/analyze-news", methods=["POST"])
def analyze_news():
    data = request.json
    text = data.get("text", "")

    if not text:
        return jsonify({"sentiment": "neutral", "summary": "Нет новостей для анализа сегодня."})

    # Используем TextBlob для определения настроения текста
    analysis = TextBlob(text)
    # polarity дает число от -1.0 (негатив) до 1.0 (позитив)
    polarity = analysis.sentiment.polarity

    if polarity > 0.02:
        sentiment = "positive"
        summary = "На рынке наблюдается позитивный фон. Новости за последние 24 часа указывают на оптимизм инвесторов."
    elif polarity < -0.02:
        sentiment = "negative"
        summary = "В новостях преобладает негатив. Возможны панические настроения или давление продавцов."
    else:
        sentiment = "neutral"
        summary = "Новостной фон спокойный. Серьезных потрясений или резких позитивных инфоповодов не зафиксировано."

    return jsonify({
        "sentiment": sentiment,
        "summary": summary
    })

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5001)