using Aluki.Runtime.Abstractions.Channels.WhatsApp;
using Aluki.Runtime.Capture.Channels.WhatsApp;
using Xunit;

namespace Aluki.Runtime.UnitTests;

[Trait("Category", "Unit")]
public sealed class MetaWebhookMapperTests
{
    private const string ContactsDefault = "[{\"profile\":{\"name\":\"Tester\"},\"wa_id\":\"5215555555555\"}]";

    private const string Template = """
    {
      "object":"whatsapp_business_account",
      "entry":[{"id":"WABA","changes":[{"field":"messages","value":{
        "messaging_product":"whatsapp",
        "metadata":{"display_phone_number":"15550000000","phone_number_id":"PNID"},
        "contacts":@@CONTACTS@@,
        "messages":[@@MESSAGES@@]
      }}]}]
    }
    """;

    private static string Envelope(string messageJson, string contactsJson = ContactsDefault) =>
        Template.Replace("@@CONTACTS@@", contactsJson).Replace("@@MESSAGES@@", messageJson);

    [Fact]
    public void Maps_text_message()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.text1","timestamp":"1690000000","type":"text","text":{"body":"hola"}}
            """);

        var result = MetaWebhookMapper.Map(json);

        var e = Assert.Single(result);
        Assert.Equal("wamid.text1", e.ProviderMessageId);
        Assert.Equal("whatsapp", e.SourceChannel);
        Assert.Equal("5215555555555", e.Sender.ExternalUserId);
        Assert.Equal("Tester", e.Sender.DisplayName);
        Assert.Equal(CapturePayloadType.Text, e.Payload.Type);
        Assert.Equal("hola", e.Payload.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1690000000), e.OccurredAtUtc);
    }

    [Fact]
    public void Maps_image_message_to_media_metadata()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.img","timestamp":"1690000001","type":"image",
             "image":{"id":"MEDIA123","mime_type":"image/jpeg","sha256":"abc"}}
            """);

        var e = Assert.Single(MetaWebhookMapper.Map(json));
        Assert.Equal(CapturePayloadType.Image, e.Payload.Type);
        Assert.NotNull(e.Payload.Media);
        Assert.Equal("image", e.Payload.Media!.MediaType);
        Assert.Equal("image/jpeg", e.Payload.Media.ContentType);
        Assert.Equal("MEDIA123", e.Payload.Media.ProviderMediaId);
        Assert.Null(e.Payload.Media.MediaRefUri); // binary download is async/out of scope here
    }

    [Fact]
    public void Maps_audio_message()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.aud","timestamp":"1690000002","type":"audio",
             "audio":{"id":"AUD1","mime_type":"audio/ogg"}}
            """);

        var e = Assert.Single(MetaWebhookMapper.Map(json));
        Assert.Equal(CapturePayloadType.Audio, e.Payload.Type);
        Assert.Equal("AUD1", e.Payload.Media!.ProviderMediaId);
    }

    [Fact]
    public void Maps_forwarded_text_to_forwarded_type()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.fwd","timestamp":"1690000003","type":"text",
             "text":{"body":"fwd"},"context":{"forwarded":true}}
            """);

        var e = Assert.Single(MetaWebhookMapper.Map(json));
        Assert.Equal(CapturePayloadType.Forwarded, e.Payload.Type);
        Assert.Equal("fwd", e.Payload.Text);
        Assert.NotNull(e.Payload.Forwarded);
    }

    [Fact]
    public void Maps_document_message_to_media_metadata()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.doc","timestamp":"1690000004","type":"document",
             "document":{"id":"D1","mime_type":"application/pdf","filename":"file.pdf","caption":"report"}}
            """);

        var e = Assert.Single(MetaWebhookMapper.Map(json));
        Assert.Equal(CapturePayloadType.Document, e.Payload.Type);
        Assert.Equal("document", e.Payload.Media!.MediaType);
        Assert.Equal("application/pdf", e.Payload.Media.ContentType);
        Assert.Equal("D1", e.Payload.Media.ProviderMediaId);
        Assert.Equal("report", e.Payload.Text); // caption
    }

    [Fact]
    public void Maps_unsupported_type()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.stk","timestamp":"1690000004","type":"sticker",
             "sticker":{"id":"S1","mime_type":"image/webp"}}
            """);

        var e = Assert.Single(MetaWebhookMapper.Map(json));
        Assert.Equal(CapturePayloadType.Unsupported, e.Payload.Type);
    }

    [Fact]
    public void Ignores_status_only_payloads()
    {
        var json = """
        {"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"1","phone_number_id":"P"},
          "statuses":[{"id":"wamid.x","status":"delivered","timestamp":"1690000000","recipient_id":"5215555555555"}]
        }}]}]}
        """;

        Assert.Empty(MetaWebhookMapper.Map(json));
    }

    [Fact]
    public void Maps_multiple_messages()
    {
        var json = Envelope("""
            {"from":"5215555555555","id":"wamid.1","timestamp":"1690000000","type":"text","text":{"body":"a"}},
            {"from":"5215555555555","id":"wamid.2","timestamp":"1690000001","type":"text","text":{"body":"b"}}
            """);

        Assert.Equal(2, MetaWebhookMapper.Map(json).Count);
    }

    [Fact]
    public void Empty_or_blank_returns_empty()
    {
        Assert.Empty(MetaWebhookMapper.Map(""));
        Assert.Empty(MetaWebhookMapper.Map("{}"));
    }
}
