CREATE TABLE IF NOT EXISTS users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(100) NOT NULL UNIQUE,
    first_name VARCHAR(100) DEFAULT '',
    last_name VARCHAR(100) DEFAULT '',
    email VARCHAR(255) NOT NULL UNIQUE,
    country VARCHAR(100) DEFAULT '',
    password_hash VARCHAR(255) NOT NULL
);

CREATE TABLE IF NOT EXISTS portfolios (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS coins (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    symbol VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(255) NOT NULL,
    market_rank INT DEFAULT 0,
    circulating_supply DECIMAL(30, 8) DEFAULT 0,
    max_supply DECIMAL(30, 8) DEFAULT 0
);

CREATE TABLE IF NOT EXISTS coin_market_data (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    coin_id BIGINT NOT NULL,
    price DECIMAL(20, 8) NOT NULL,
    market_cap DECIMAL(30, 2) DEFAULT 0,
    volume_24h DECIMAL(30, 2) DEFAULT 0,
    percent_change_24h DECIMAL(10, 4) DEFAULT 0,
    recorded_at DATETIME NOT NULL,
    UNIQUE KEY uq_coin_recorded (coin_id, recorded_at),
    FOREIGN KEY (coin_id) REFERENCES coins(id) ON DELETE CASCADE,
    INDEX idx_coin_recorded (coin_id, recorded_at)
);

CREATE TABLE IF NOT EXISTS coin_ai_analysis (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    coin_id BIGINT NOT NULL,
    period VARCHAR(10) NOT NULL DEFAULT '30d',
    trend VARCHAR(50),
    risk_level VARCHAR(50),
    forecast TEXT,
    explanation TEXT,
    UNIQUE KEY uq_coin_period (coin_id, period),
    FOREIGN KEY (coin_id) REFERENCES coins(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS portfolio_assets (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    portfolio_id BIGINT NOT NULL,
    coin_id BIGINT NOT NULL,
    quantity DECIMAL(20, 8) NOT NULL,
    buy_price DECIMAL(20, 8) NOT NULL,
    FOREIGN KEY (portfolio_id) REFERENCES portfolios(id) ON DELETE CASCADE,
    FOREIGN KEY (coin_id) REFERENCES coins(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS portfolio_ai_analysis (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    portfolio_id BIGINT NOT NULL,
    risk_level VARCHAR(50) DEFAULT 'medium',
    explanation TEXT,
    recommendations TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (portfolio_id) REFERENCES portfolios(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS user_favorites (
    user_id INT NOT NULL,
    coin_id BIGINT NOT NULL,
    PRIMARY KEY (user_id, coin_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (coin_id) REFERENCES coins(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS favorite_groups (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    name VARCHAR(255) NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS price_alerts (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    coin_id BIGINT NOT NULL,
    condition_type VARCHAR(20) NOT NULL,
    target_price DECIMAL(20, 8) NOT NULL,
    is_triggered TINYINT(1) DEFAULT 0,
    is_read TINYINT(1) DEFAULT 0,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (coin_id) REFERENCES coins(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS news (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    title TEXT NOT NULL,
    summary TEXT,
    source VARCHAR(255),
    url VARCHAR(500) UNIQUE,
    sentiment VARCHAR(50) DEFAULT 'neutral',
    published_at DATETIME
);

CREATE TABLE IF NOT EXISTS market_ai_analysis (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    sentiment VARCHAR(50),
    summary TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);
