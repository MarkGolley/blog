using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MyBlog.Services;

public sealed record NotificationEmailRequest(
    string SubscriberId,
    string ToEmail,
    string PostTitle,
    string PostUrl,
    string UnsubscribeUrl);

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

    public async Task<IReadOnlyDictionary<string, bool>> SendNewPostNotificationsAsync(
        IReadOnlyList<NotificationEmailRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (requests.Count == 0)
        {
            return results;
        }

        var emailAddress = Environment.GetEnvironmentVariable("ICLOUD_EMAIL");
        var emailPassword = Environment.GetEnvironmentVariable("ICLOUD_APP_PASSWORD");
        if (string.IsNullOrWhiteSpace(emailAddress) || string.IsNullOrWhiteSpace(emailPassword))
        {
            _logger.LogWarning("Subscriber notification emails were skipped because email credentials are not configured.");
            foreach (var request in requests)
            {
                results[request.SubscriberId] = false;
            }

            return results;
        }

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.mail.me.com", 587, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(emailAddress, emailPassword, cancellationToken);

            foreach (var request in requests)
            {
                var emailMessage = BuildNewPostNotificationMessage(
                    emailAddress,
                    request.ToEmail,
                    request.PostTitle,
                    request.PostUrl,
                    request.UnsubscribeUrl);

                try
                {
                    await client.SendAsync(emailMessage, cancellationToken);
                    results[request.SubscriberId] = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to send batched subscriber notification to {Email}.",
                        request.ToEmail);
                    results[request.SubscriberId] = false;
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send batched subscriber notifications.");
            foreach (var request in requests)
            {
                results[request.SubscriberId] = false;
            }

            return results;
        }
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

            var emailMessage = BuildPlainTextMessage(emailAddress, toEmail, subject, body);

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

    private static MimeMessage BuildPlainTextMessage(
        string fromEmail,
        string toEmail,
        string subject,
        string body)
    {
        var emailMessage = new MimeMessage();
        emailMessage.From.Add(new MailboxAddress("MyBlog", fromEmail));
        emailMessage.To.Add(new MailboxAddress(toEmail, toEmail));
        emailMessage.Subject = subject;
        emailMessage.Body = new TextPart("plain")
        {
            Text = body
        };

        return emailMessage;
    }

    private static MimeMessage BuildNewPostNotificationMessage(
        string fromEmail,
        string toEmail,
        string postTitle,
        string postUrl,
        string unsubscribeUrl)
    {
        var subject = $"New post on MyBlog: {postTitle}";
        var body =
            $"A new post is live on MyBlog.\n\nTitle: {postTitle}\nRead: {postUrl}\n\nUnsubscribe: {unsubscribeUrl}";
        return BuildPlainTextMessage(fromEmail, toEmail, subject, body);
    }
}
