// The MIT License (MIT)
// 
// Copyright (c) 2014 James White of Gbase.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Gbase.NLog.JsonTarget
{
    internal sealed class JsonPoster : IDisposable
    {
        public int ActivePosts = 0;
        private HttpClient _httpClient;

        public JsonPoster()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.ConnectionClose = true;
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public bool ThrowExceptionsOnFailedPost { get; set; }

        public void Dispose()
        {
            if (_httpClient == null)
                return;
          
            Debug.WriteLine("Disposing..");

            _httpClient.Dispose();
            _httpClient = null;
        }

        public JsonPoster AddHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders.Add(name, value);
            return this;
        }

        public async void Post(Uri uri, string json)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                
                var posts = Interlocked.Increment(ref ActivePosts);

                Debug.WriteLine("JsonPoster posting ({0})...", posts);

                var response = await _httpClient.PostAsync(uri, content);//.WithTimeout(_httpClient.Timeout);

                if (ThrowExceptionsOnFailedPost)
                    response.EnsureSuccessStatusCode();
            }
            catch (TaskCanceledException cancelledEx)
            {
                Debug.WriteLine("Task #{0} cancelled..", cancelledEx.Task.Id);
            }
            finally
            {
                var posts = Interlocked.Decrement(ref ActivePosts);

                Debug.WriteLine("JsonPoster completed ({0})...", posts);
            }
        }
    }

    public static class TaskExtensions
    {
        public static Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            var delay = task.ContinueWith(t => t.Result
                , new CancellationTokenSource(timeout).Token);
            return Task.WhenAny(task, delay).Unwrap();
        }
    }
}