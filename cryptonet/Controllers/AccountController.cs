using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using cryptonet.Data;
using cryptonet.Models;
using System.Security.Cryptography;
using System.Text;

namespace cryptonet.Controllers
{
    public class AccountController : Controller
    {
        private readonly DbConnectionFactory _db;

        public AccountController(DbConnectionFactory db)
        {
            _db = db;
        }

        // =========================
        // РЕГИСТРАЦИЯ
        // =========================

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Register(RegisterViewModel model)
        {
            // 1. Проверка полей модели
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // 2. Проверка совпадения паролей
            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Пароли не совпадают");
                return View(model);
            }

            // 3. Проверка сложности пароля
            if (model.Password.Length < 6 || !model.Password.Any(char.IsDigit))
            {
                ModelState.AddModelError("Password", "Пароль должен быть от 6 символов и содержать цифру");
                return View(model);
            }

            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            string passwordHash = HashPassword(model.Password);

            var command = new MySqlCommand(@"
                INSERT INTO users (username, first_name, last_name, email, country, password_hash)
                VALUES (@username, @firstName, @lastName, @email, @country, @passwordHash)
            ", connection);

            command.Parameters.AddWithValue("@username", model.Username);
            command.Parameters.AddWithValue("@firstName", model.FirstName ?? "");
            command.Parameters.AddWithValue("@lastName", model.LastName ?? "");
            command.Parameters.AddWithValue("@email", model.Email);
            command.Parameters.AddWithValue("@country", model.Country ?? "");
            command.Parameters.AddWithValue("@passwordHash", passwordHash);

            try
            {
                command.ExecuteNonQuery();
                long userId = command.LastInsertedId;

                // Автоматический вход после регистрации
                HttpContext.Session.SetString("Username", model.Username);
                HttpContext.Session.SetInt32("UserId", (int)userId);

                return RedirectToAction("Dashboard", "Market");
            }
            catch (MySqlException)
            {
                ModelState.AddModelError("", "Пользователь с таким логином или email уже существует");
                return View(model);
            }
            // Если что-то пошло совсем не так, возвращаем вью с ошибкой
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Произошла ошибка при регистрации. Попробуйте позже.");
                return View(model);
            }
        }

        // =========================
        // ВХОД
        // =========================

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            using var connection = (MySqlConnection)_db.CreateConnection();
            connection.Open();

            var command = new MySqlCommand(@"
                SELECT id, username, password_hash
                FROM users
                WHERE username = @login OR email = @login
            ", connection);

            command.Parameters.AddWithValue("@login", model.UsernameOrEmail);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                ModelState.AddModelError("", "Неверный логин или пароль");
                return View(model);
            }

            string storedHash = reader.GetString("password_hash");

            if (storedHash != HashPassword(model.Password))
            {
                ModelState.AddModelError("", "Неверный логин или пароль");
                return View(model);
            }

            HttpContext.Session.SetString("Username", reader.GetString("username"));
            HttpContext.Session.SetInt32("UserId", reader.GetInt32("id"));

            return RedirectToAction("Dashboard", "Market");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}