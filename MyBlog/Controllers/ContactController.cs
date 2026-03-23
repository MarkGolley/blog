using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace MyBlog.Controllers;

public class ContactController(ILogger<ContactController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View(new ContactFormModel());
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("contactWrites")]
    public async Task<IActionResult> Index(ContactFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var emailAddress = Environment.GetEnvironmentVariable("ICLOUD_EMAIL");
            var emailPassword = Environment.GetEnvironmentVariable("ICLOUD_APP_PASSWORD");

            if (string.IsNullOrEmpty(emailAddress) || string.IsNullOrEmpty(emailPassword))
            {
                logger.LogError("Email credentials are not set in environment variables.");
                ViewBag.Error = "Email system is not configured. Please try again later.";
                return View(model);
            }
            
            // Build email
            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress("MyBlog Contact Form", emailAddress));
            emailMessage.ReplyTo.Add(new MailboxAddress(model.Name, model.Email));
            emailMessage.To.Add(new MailboxAddress("Mark Golley", emailAddress));
            emailMessage.Subject = $"New Contact Form Message from {model.Name}";
            emailMessage.Body = new TextPart("plain")
            {
                Text = $"Name: {model.Name}\nEmail: {model.Email}\n\nMessage:\n{model.Message}"
            };

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.mail.me.com", 587, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(emailAddress, emailPassword, cancellationToken);
            await client.SendAsync(emailMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            ViewBag.MessageSent = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending contact form email.");
            ViewBag.Error = "Sorry, there was a problem sending your message. Please try again later.";
        }

        return View(new ContactFormModel());
    }
}
