CREATE TABLE IF NOT EXISTS clients (
    id SERIAL PRIMARY KEY,
    keycloak_username VARCHAR(50) UNIQUE NOT NULL,
    full_name VARCHAR(100),
    email VARCHAR(100),
    prosthesis_serial_number VARCHAR(50)
);

-- Очистка на случай перезапуска, чтобы не было дублей при тестах
TRUNCATE TABLE clients;

INSERT INTO clients (keycloak_username, full_name, email, prosthesis_serial_number) VALUES
('user1', 'User One', 'user1@example.com', 'SN-USER-01'),
('user2', 'User Two', 'user2@example.com', 'SN-USER-02'),
('prothetic1', 'Prothetic One', 'prothetic1@example.com', 'SN-PRO-01'),
('prothetic2', 'Prothetic Two', 'prothetic2@example.com', 'SN-PRO-02');
