namespace Zenith.App.Services;

/// <summary>
/// Provides the authentic Edge 1507 Start Page HTML (zenith://newtab)
/// matching the exact layout, colors, and controls of Windows 10 build 1507 Spartan/Edge.
/// </summary>
public static class StartPageService
{
    public const string StartPageUrl = "zenith://newtab";

    public static string GetStartPageHtml()
    {
        string searchBaseUrl = SettingsService.SearchEngine switch
        {
            "Bing" => "https://www.bing.com/search?q=",
            "Google" => "https://www.google.com/search?q=",
            _ => "https://duckduckgo.com/?q="
        };

        string greeting = LangService.T("addressbar.placeholder");
        string placeholder = LangService.T("addressbar.search_placeholder");

        string html = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>__START_PAGE_TITLE__</title>
    <style>
        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        body {
            font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
            background-color: #F2F2F2;
            color: #000000;
            display: flex;
            flex-direction: column;
            align-items: center;
            min-height: 100vh;
            user-select: none;
            overflow-x: hidden;
        }

        /* Top Bar with Account Icon (matching startpage.png top-right) */
        .top-account-bar {
            width: 100%;
            display: flex;
            justify-content: flex-end;
            padding: 16px 24px;
        }

        .account-btn {
            background: transparent;
            border: none;
            cursor: pointer;
            width: 36px;
            height: 36px;
            display: flex;
            align-items: center;
            justify-content: center;
            color: #000000;
            border-radius: 2px;
            transition: background-color 0.1s;
        }

        .account-btn:hover {
            background-color: #E5E5E5;
        }

        .account-btn svg {
            width: 22px;
            height: 22px;
            fill: currentColor;
        }

        /* Center content */
        .center-container {
            display: flex;
            flex-direction: column;
            align-items: center;
            margin-top: 10vh;
            width: 100%;
            max-width: 600px;
            padding: 0 20px;
        }

        /* "Where to next?" */
        .greeting {
            font-size: 38px;
            font-weight: 300;
            color: #000000;
            margin-bottom: 24px;
            letter-spacing: -0.4px;
            text-align: center;
        }

        /* Search Box (matching startpage.png) */
        .search-container {
            width: 100%;
            max-width: 512px;
            height: 38px;
            background: #FFFFFF;
            border: 1px solid #D0D0D0;
            display: flex;
            align-items: center;
            box-shadow: 0 1px 2px rgba(0,0,0,0.04);
            transition: border-color 0.15s;
        }

        .search-container:focus-within {
            border: 2px solid #0078D7;
        }

        .search-icon {
            width: 38px;
            height: 38px;
            display: flex;
            align-items: center;
            justify-content: center;
            color: #767676;
            flex-shrink: 0;
        }

        .search-icon svg {
            width: 16px;
            height: 16px;
            fill: currentColor;
        }

        .search-input {
            flex: 1;
            height: 100%;
            border: none;
            outline: none;
            font-family: 'Segoe UI', sans-serif;
            font-size: 14px;
            color: #000000;
            background: transparent;
            padding: 0 8px;
            cursor: text;
            user-select: text;
            -webkit-user-select: text;
        }

        .search-input::placeholder {
            color: #767676;
            font-weight: 400;
        }

        /* Submit button with right arrow */
        .submit-btn {
            width: 36px;
            height: 100%;
            background: #0078D7;
            border: none;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            color: #FFFFFF;
            flex-shrink: 0;
            transition: background-color 0.1s;
        }

        .submit-btn:hover {
            background-color: #1A86DF;
        }

        .submit-btn:active {
            background-color: #006CC3;
        }

        .submit-btn svg {
            width: 16px;
            height: 16px;
            fill: #FFFFFF;
        }

        /* Cleared state when top address bar is active */
        body.clear-content .greeting,
        body.clear-content .search-container,
        body.clear-content .top-account-bar {
            opacity: 0;
            pointer-events: none;
            transition: opacity 0.15s ease-out;
        }
    </style>
</head>
<body onclick="onBodyClick(event)">
    <!-- Top right profile icon matching startpage.png -->
    <div class="top-account-bar">
        <button class="account-btn" title="Profile">
            <svg viewBox="0 0 24 24">
                <path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z M19 8v2h-2V8h-2V6h2V4h2v2h2v2z"/>
            </svg>
        </button>
    </div>

    <div class="center-container">
        <!-- "Where to next?" -->
        <h1 class="greeting">__GREETING__</h1>

        <!-- Search Bar matching startpage.png -->
        <form class="search-container" onsubmit="handleSearch(event)">
            <div class="search-icon">
                <svg viewBox="0 0 24 24">
                    <path d="M15.5 14h-.79l-.28-.27A6.471 6.471 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z"/>
                </svg>
            </div>
            <input type="text"
                   id="searchInput"
                   class="search-input"
                   placeholder="__PLACEHOLDER__"
                   autocomplete="off" />
            <button type="submit" class="submit-btn" title="Search">
                <svg viewBox="0 0 24 24">
                    <path d="M5 13h11.86l-5.43 5.43 1.42 1.42L21.14 12l-8.29-7.85-1.42 1.42L16.86 11H5v2z"/>
                </svg>
            </button>
        </form>
    </div>

    <script>
        function onBodyClick(e) {
            if (document.body.classList.contains('clear-content')) {
                if (window.CefSharp && CefSharp.PostMessage) {
                    CefSharp.PostMessage('blur_address_bar');
                }
            }
        }

        window.setStartPageCleared = function(cleared) {
            if (cleared) {
                document.body.classList.add('clear-content');
            } else {
                document.body.classList.remove('clear-content');
            }
        };

        function handleSearch(e) {
            if (e) e.preventDefault();
            const input = document.getElementById('searchInput').value.trim();
            if (!input) return false;

            let url = input;
            if (/^[a-zA-Z]+:\/\//.test(input)) {
                url = input;
            } else if (input.includes('.') && !input.includes(' ')) {
                url = 'https://' + input;
            } else {
                url = '__SEARCH_URL__' + encodeURIComponent(input);
            }
            window.location.href = url;
            return false;
        }
    </script>
</body>
</html>
""";
        return html
            .Replace("__START_PAGE_TITLE__", LangService.T("tab.newtab"))
            .Replace("__GREETING__", greeting)
            .Replace("__PLACEHOLDER__", placeholder)
            .Replace("__SEARCH_URL__", searchBaseUrl);
    }
}
