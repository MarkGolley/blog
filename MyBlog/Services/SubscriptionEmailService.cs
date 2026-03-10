using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MyBlog.Services;

public class SubscriptionEmailService
{
    private readonly ILogger<SubscriptionEmailService> _logger;

    public SubscriptionEmailService(ILogger<SubscriptionEmailService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> SendConfirmationEmailAsync(string toEmail, string confirmUrl, string unsubscribeUrl)
    {
        var subject = "Confirm your MyBlog subscription";
        var body =
            $"Please confirm your subscription to MyBlog updates.\n\nConfirm: {confirmUrl}\n\nIf this wasn't you, ignore this email or unsubscribe here: {unsubscribeUrl}";

        return await SendEmailAsync(toEmail, subject, body);
    }

    public async Task<bool> SendNewPostNotificationAsync(
        string toEmail,
        string postTitle,
        string postUrl,
        string unsubscribeUrl)
    {
        var subject = $"New post on MyBlog: {postTitle}";
        var body =
            $"A new post is live on MyBlog.\n\nTitle: {postTitle}\nRead: {postUrl}\n\nUnsubscribe: {unsubscribeUrl}";

        return await SendEmailAsync(toEmail, subject, body);
    }

    private async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
    {
        try
        {
            var emailAddress = Environment.GetEnvironmentVariable("ICLOUD_EMAIL");
            var emailPassword = Environment.GetEnvironmentVariable("ICLOUD_APP_PASSWORD");

            if (string.IsNullOrWhiteSpace(emailAddress) || string.IsNullOrWhiteSpace(emailPassword))
            {
                _logger.LogWarning(
                    "Subscription email not sent because email credentials are not configured.");
                return false;
            }

            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress("MyBlog", emailAddress));
            emailMessage.To.Add(new MailboxAddress(toEmail, toEmail));
            emailMessage.Subject = subject;
            emailMessage.Body = new TextPart("plain")
            {
                Text = body
            };

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.mail.me.com", 587, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(emailAddress, emailPassword);
            await client.SendAsync(emailMessage);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send subscription email to {Email}.", toEmail);
            return false;
        }
    }
}
