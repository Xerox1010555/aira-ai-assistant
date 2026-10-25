using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace AiraMobile;

/// <summary>
/// Aira for Android (companion build). One dark glass-flavoured chat
/// screen: brand header, transcript of glass bubbles, composer and a
/// live status strip. The OpenAI key is prompted once and stored in
/// device-private SharedPreferences. Visuals follow the desktop
/// "Midnight Violet" theme family; the mic halo is red across themes.
/// </summary>
[Activity(Label = "Aira",
          MainLauncher = true,
          Theme = "@android:style/Theme.Material.NoActionBar",
          ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait,
          ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize)]
public class MainActivity : Activity
{
    private const string OpenAiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4o-mini";
    private const string SystemPrompt =
        "You are Aira, a concise, tactical AI assistant. Keep answers short, useful and friendly.";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private LinearLayout _messages = null!;
    private ScrollView _scroll = null!;
    private EditText _input = null!;
    private TextView _status = null!;
    private Button _send = null!;
    private bool _busy;

    private readonly List<(string role, string content)> _history = new();

    private ISharedPreferences Prefs => GetSharedPreferences("aira", FileCreationMode.Private)!;

    private string? ApiKey => Prefs.GetString("api_key", null);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        EnsureApiKey();
    }

    // ============================ UI ============================

    private void BuildUi()
    {
        var backdrop = Color.Argb(235, 13, 18, 32);      // glass backdrop
        var panel = Color.Argb(255, 18, 24, 50);         // header / composer panel
        var accent = Color.Argb(255, 109, 133, 247);     // violet accent
        var muted = Color.Argb(255, 148, 160, 190);      // muted text
        var halo = Color.Argb(255, 255, 77, 77);         // signature RED halo

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(backdrop);

        // ---- brand header (accent bar + wordmark) ----
        var header = new LinearLayout(this) { Orientation = Orientation.Vertical };
        header.SetBackgroundColor(panel);
        header.SetPadding(38, 46, 38, 30);

        var topRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var chip = new TextView(this) { Text = "A" };
        chip.SetTextSize(ComplexUnitType.Sp, 18);
        chip.Typeface = Typeface.Create("sans-serif", TypefaceStyle.Bold);
        chip.SetTextColor(Color.White);
        chip.Gravity = GravityFlags.Center;
        chip.Background = MakeCircle(accent);
        topRow.AddView(chip, new LinearLayout.LayoutParams(46, 46));

        var brand = new TextView(this) { Text = "AIRA" };
        brand.SetTextSize(ComplexUnitType.Sp, 26);
        brand.Typeface = Typeface.Create("sans-serif", TypefaceStyle.Bold);
        brand.SetTextColor(Color.White);
        brand.Gravity = GravityFlags.CenterVertical;
        var brandLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent);
        brandLp.LeftMargin = 16;
        topRow.AddView(brand, brandLp);
        header.AddView(topRow, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var subtitle = new TextView(this)
        {
            Text = "مساعدك الذكي المحمول — إصدار أندرويد من تطبيق ويندوز",
        };
        subtitle.SetTextSize(ComplexUnitType.Sp, 12);
        subtitle.SetTextColor(muted);
        subtitle.SetPadding(0, 8, 0, 0);
        header.AddView(subtitle);

        root.AddView(header, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // ---- status strip ----
        _status = new TextView(this) { Text = "Idle" };
        _status.SetTextSize(ComplexUnitType.Sp, 11);
        _status.SetTextColor(halo);
        _status.SetPadding(38, 18, 38, 8);
        root.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // ---- transcript ----
        _scroll = new ScrollView(this) { VerticalScrollBarEnabled = false };
        _messages = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _messages.SetPadding(26, 24, 26, 26);
        _scroll.AddView(_messages, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        root.AddView(_scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        // ---- composer ----
        var composer = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        composer.SetBackgroundColor(panel);
        composer.SetPadding(26, 20, 26, 20);

        _input = new EditText(this)
        {
            Hint = "اكتب رسالتك…",
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences,
            ImeOptions = ImeAction.Send,
        };
        _input.SetTextSize(ComplexUnitType.Sp, 14);
        _input.SetTextColor(Color.White);
        _input.SetHintTextColor(Color.Argb(255, 120, 132, 160));
        _input.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(accent);
        _input.EditorAction += (_, args) =>
        {
            if (args.ActionId == ImeAction.Send)
            {
                RunOnUiThread(async () => await SendAsync());
                args.Handled = true;
            }
        };
        composer.AddView(_input, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            RightMargin = 14,
        });

        _send = new Button(this) { Text = "Send", Enabled = false };
        _send.SetTextSize(ComplexUnitType.Sp, 14);
        _send.SetTextColor(Color.White);
        _send.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(accent);
        _send.Click += async (_, _) => await SendAsync();
        composer.AddView(_send, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        root.AddView(composer, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        SetContentView(root);
        AddBubble("Aira", "أهلاً بك! أنا أيرا ✨ اكتب رسالتك وسأرد مباشرة.", false);
    }

    private static GradientDrawable MakeCircle(Color color)
    {
        var d = new GradientDrawable();
        d.SetShape(ShapeType.Oval);
        d.SetColor(color);
        return d;
    }

    private void AddBubble(string author, string text, bool isUser)
    {
        RunOnUiThread(() =>
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Vertical };
            var rowLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                BottomMargin = 18,
            };
            if (!isUser)
            {
                rowLp.RightMargin = 60;
            }
            else
            {
                rowLp.LeftMargin = 60;
            }

            var bubble = new TextView(this)
            {
                Text = text,
            };
            bubble.SetTextSize(ComplexUnitType.Sp, 14);
            bubble.SetLineSpacing(0f, 1.25f);
            bubble.Clickable = false;
            bubble.SetPadding(36, 26, 36, 26);

            if (isUser)
            {
                bubble.Background = MakeBubble(Color.Argb(230, 94, 123, 255));
                bubble.SetTextColor(Color.White);
            }
            else
            {
                bubble.Background = MakeBubble(Color.Argb(216, 24, 31, 51));
                bubble.SetTextColor(Color.Argb(255, 226, 232, 255));
            }

            row.AddView(bubble, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            var head = new TextView(this)
            {
                Text = $"{author}   {DateTime.Now:HH:mm}",
            };
            head.SetTextSize(ComplexUnitType.Sp, 10);
            head.SetTextColor(Color.Argb(255, 120, 132, 160));
            head.SetPadding(4, 0, 4, 6);
            row.AddView(head, 0, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

            _messages.AddView(row);
            _scroll.Post(() => _scroll.FullScroll(FocusSearchDirection.Down));
        });
    }

    private static GradientDrawable MakeBubble(Color color)
    {
        var d = new GradientDrawable();
        d.SetShape(ShapeType.Rectangle);
        d.SetColor(color);
        d.SetStroke(1, Color.Argb(51, 255, 255, 255));
        d.SetCornerRadius(52);
        return d;
    }

    private void SetStatus(string s)
    {
        RunOnUiThread(() => _status.Text = s);
    }

    // ============================ key prompt ============================

    private void EnsureApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            RunOnUiThread(() => _send.Enabled = true);
            return;
        }

        RunOnUiThread(() =>
        {
            var input = new EditText(this) { Hint = "sk-..." };
            input.SetSingleLine(true);
            input.SetTextColor(Color.White);

            var wrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
            wrap.SetPadding(64, 30, 64, 10);
            var hint = new TextView(this)
            {
                Text = "أدخل مفتاح OpenAI (sk-…) — يُخزَّن داخل جهازك فقط ويُرسل عبر HTTPS.",
            };
            hint.SetTextSize(ComplexUnitType.Sp, 13);
            hint.SetTextColor(Color.Argb(255, 148, 160, 190));
            wrap.AddView(hint);
            wrap.AddView(input);

            new AlertDialog.Builder(this)
                .SetTitle("Aira — API key")
                .SetView(wrap)
                .SetPositiveButton("Save", (_, _) =>
                {
                    var value = input.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        Prefs.Edit()?.PutString("api_key", value)?.Apply();
                        _send.Enabled = true;
                        SetStatus("Idle");
                    }
                    else
                    {
                        SetStatus("No API key");
                    }
                })
                .SetNegativeButton("Later", (_, _) => SetStatus("No API key"))
                .SetCancelable(false)
                .Show();
        });
    }

    // ============================ AI call ============================

    private async Task SendAsync()
    {
        var text = _input?.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _busy)
        {
            return;
        }

        _busy = true;
        RunOnUiThread(() =>
        {
            _input!.Text = string.Empty;
            _send.Enabled = false;
        });

        AddBubble("You", text!, true);
        SetStatus("Thinking…");

        try
        {
            var reply = await AskAsync(text!);
            AddBubble("Aira", reply, false);
            SetStatus("Idle");
        }
        catch (Exception ex)
        {
            AddBubble("Aira", $"تعذّر الاتصال: {ex.Message}", false);
            SetStatus("Error");
        }
        finally
        {
            _busy = false;
            RunOnUiThread(() => _send.Enabled = true);
        }
    }

    private async Task<string> AskAsync(string userText)
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new Exception("لا يوجد مفتاح API — أدخله عند بدء التشغيل.");
        }

        _history.Add(("user", userText));
        if (_history.Count > 20)
        {
            _history.RemoveRange(0, _history.Count - 20);
        }

        var msgs = new (string role, string content)[_history.Count + 1];
        msgs[0] = ("system", SystemPrompt);
        _history.CopyTo(msgs, 1);

        var payload = new
        {
            model = DefaultModel,
            messages = msgs.Select(m => new { role = m.role, content = m.content }).ToArray(),
            temperature = 0.4,
        };

        var req = new HttpRequestMessage(HttpMethod.Post, OpenAiEndpoint);
        req.Headers.Add("Authorization", $"Bearer {key}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new Exception("Unexpected response shape from OpenAI.");
        }

        var content = choices[0].GetProperty("message")
            .GetProperty("content").GetString() ?? string.Empty;
        _history.Add(("assistant", content));
        return content.Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
