using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

namespace Conquest.Services.Email;

public class SesEmailService(IAmazonSimpleEmailService ses, IConfiguration config, ILogger<SesEmailService> logger) : IEmailService
{
    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var fromAddress = config["Email:FromAddress"] ?? "noreply@conquest-app.com";

        var sendRequest = new SendEmailRequest
        {
            Source = fromAddress,
            Destination = new Destination
            {
                ToAddresses = [to]
            },
            Message = new Message
            {
                Subject = new Content(subject),
                Body = new Body
                {
                    Html = new Content
                    {
                        Charset = "UTF-8",
                        Data = body
                    },
                    Text = new Content
                    {
                        Charset = "UTF-8",
                        Data = body // Simple fallback
                    }
                }
            }
        };

        try
        {
            logger.LogInformation("Sending email to {To} via SES...", to);
            var response = await ses.SendEmailAsync(sendRequest);
            logger.LogInformation("Email sent to {To}. MessageId: {MessageId}", to, response.MessageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To}", to);
            throw; // Let caller handle failure or suppress
        }
    }
}
