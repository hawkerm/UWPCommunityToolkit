using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Microsoft.Toolkit.Uwp.UI.Extensions
{
    public static class WebViewEx
    {
        /// <summary>
        /// HasLoaded will be true once a WebView has completed its initial first-time navigation.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static bool GetHasLoaded(Windows.UI.Xaml.Controls.WebView obj)
        {
            return (bool)obj.GetValue(HasLoadedProperty);
        }

        private static void SetHasLoaded(Windows.UI.Xaml.Controls.WebView obj, bool value)
        {
            obj.SetValue(HasLoadedProperty, value);
        }

        // Using a DependencyProperty as the backing store for HasLoaded.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty HasLoadedProperty =
            DependencyProperty.RegisterAttached("HasLoaded", typeof(bool), typeof(WebViewEx), new PropertyMetadata(false));

        /// <summary>
        /// Set HideOnLoad to true to only display the WebView when it has finished loading its initial page.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static bool GetHideOnLoad(DependencyObject obj)
        {
            return (bool)obj.GetValue(HideOnLoadProperty);
        }

        public static void SetHideOnLoad(DependencyObject obj, bool value)
        {
            obj.SetValue(HideOnLoadProperty, value);
        }

        // Using a DependencyProperty as the backing store for HideOnLoad.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty HideOnLoadProperty =
            DependencyProperty.RegisterAttached("HideOnLoad", typeof(bool), typeof(WebViewEx), new PropertyMetadata(false, (d, e) =>
            {
                // If we set the HideOnLoad property then
                var webview = d as Windows.UI.Xaml.Controls.WebView;

                webview.NavigationCompleted -= Webview_NavigationCompleted;
                if (e.NewValue as bool? == true && !GetHasLoaded(webview))
                {
                    // Only set the Visibility to Collapsed if we set HideOnLoad to true and if we're not already loaded
                    webview.Visibility = Visibility.Collapsed;
                }

                webview.NavigationCompleted += Webview_NavigationCompleted;
            }));

        private static void Webview_NavigationCompleted(Windows.UI.Xaml.Controls.WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            // When the WebView has had a successful Navigation Completed event, we'll toggle its visibility.
            sender.Visibility = Visibility.Visible;

            // Unregister event as we don't care about future navigations, only initial one.
            sender.NavigationCompleted -= Webview_NavigationCompleted;

            SetHasLoaded(sender, true);
        }

        public static Task<string> RunScriptAsync(
            this Windows.UI.Xaml.Controls.WebView _view,
            string script,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            return RunScriptAsync<string>(_view, script, member, file, line);
        }

        public static async Task<T> RunScriptAsync<T>(
            this Windows.UI.Xaml.Controls.WebView _view,
            string script,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0)
        {
            var fullscript = "try {\n" +
                                script +
                             "\n} catch (err) { JSON.stringify({ wv_internal_error: true, message: err.message, description: err.description, number: err.number, stack: err.stack }); }";

            if (_view.Dispatcher.HasThreadAccess)
            {
                try
                {
                    return await RunScriptHelperAsync(_view, fullscript);
                }
                catch (Exception e)
                {
                    throw new JavaScriptExecutionException(member, file, line, script, e);
                }
            }
            else
            {
                return await _view.Dispatcher.RunTaskAsync(async () =>
                {
                    try
                    {
                        return await RunScriptHelperAsync(_view, fullscript);
                    }
                    catch (Exception e)
                    {
                        throw new JavaScriptExecutionException(member, file, line, script, e);
                    }
                });
            }
        }

        private static async Task<string> RunScriptHelperAsync(Windows.UI.Xaml.Controls.WebView _view, string script)
        {
            var returnstring = await _view.InvokeScriptAsync("eval", new string[] { script });

            if (JsonObject.TryParse(returnstring, out JsonObject result))
            {
                if (result.ContainsKey("wv_internal_error") && result["wv_internal_error"].ValueType == JsonValueType.Boolean && result["wv_internal_error"].GetBoolean())
                {
                    throw new JavaScriptInnerException(result["message"].GetString(), result["stack"].GetString());
                }
            }

            return returnstring;
        }

        private static JsonSerializerSettings _settings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        public static async Task<string> InvokeScriptAsync(
            this Windows.UI.Xaml.Controls.WebView _view,
            string method,
            [CallerMemberName] string member = null,
            [CallerFilePath] string file = null,
            [CallerLineNumber] int line = 0,
            bool serialize = true,
            params object[] args) // TODO: Figure out how to actually make 'params' work here, possible?
        {
            string[] sanitizedargs;

            if (serialize)
            {
                sanitizedargs = args.Select(item =>
                {
                    if (item is int || item is double)
                    {
                        return item.ToString();
                    }
                    else if (item is string)
                    {
                        return JsonConvert.ToString(item);
                    }
                    else
                    {
                        return JsonConvert.SerializeObject(item, _settings);
                    }
                }).ToArray();
            }
            else
            {
                sanitizedargs = args.Select(item => item.ToString()).ToArray();
            }

            var script = method + "(" + string.Join(",", sanitizedargs) + ");";

            return await RunScriptAsync(_view, script, member, file, line);
        }
    }

    // TODO: Move to own class
    internal sealed class JavaScriptExecutionException : Exception
    {
        public string Script { get; private set; }

        public string Member { get; private set; }

        public string FileName { get; private set; }

        public int LineNumber { get; private set; }

        public JavaScriptExecutionException(string member, string filename, int line, string script, Exception inner)
            : base("Error Executing JavaScript Code for " + member + "\nLine " + line + " of " + filename + "\n" + script + "\n", inner)
        {
            this.Member = member;
            this.FileName = filename;
            this.LineNumber = line;
            this.Script = script;
        }
    }

    internal sealed class JavaScriptInnerException : Exception
    {
        public string JavaScriptStackTrace { get; private set; } // TODO Use Enum of JS error types https://www.w3schools.com/js/js_errors.asp

        public JavaScriptInnerException(string message, string stack)
            : base(message)
        {
            this.JavaScriptStackTrace = stack;
        }
    }
}
