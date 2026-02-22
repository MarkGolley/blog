using Microsoft.AspNetCore.Mvc;
using MyBlog.Services;
using MyBlog.Models;
using MailKit.Net.Smtp;
using MimeKit;
using System;
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
    public IActionResult Index(ContactFormModel model)
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
            emailMessage.From.Add(new MailboxAddress(model.Name, model.Email));
            emailMessage.To.Add(new MailboxAddress("Mark Golley", emailAddress));
            emailMessage.Subject = "New Contact Form Message from Your Blog";
            emailMessage.Body = new TextPart("plain")
            {
                Text = $"Name: {model.Name}\nEmail: {model.Email}\n\nMessage:\n{model.Message}"
            };

            using (var client = new SmtpClient())
            {
                client.Connect("smtp.mail.me.com", 587, false);
                client.Authenticate(emailAddress, emailPassword);
                client.Send(emailMessage);
                client.Disconnect(true);
            }

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