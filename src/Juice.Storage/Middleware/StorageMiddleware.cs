using System.Text.Json;
using System.Text.RegularExpressions;
using Juice.Storage.Abstractions;
using Juice.Storage.Dto;
using Juice.Storage.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Juice.Storage.Middleware
{
    public partial class StorageMiddleware
    {
        private RequestDelegate _next;
        private StorageMiddlewareOptions _options;

        private IStorageResolver? _resolver;
        public StorageMiddleware(RequestDelegate next,
            StorageMiddlewareOptions options)
        {
            _next = next;
            _options = options;
        }

        //this feature is available in .net 7
        //[GeneratedRegex("^(?<identity>\\/[\\w]+)(?<action>\\/[\\w]+)")]
        //private static partial Regex StorageMatcher();
        public async Task InvokeAsync(HttpContext context)
        {

            var path = context.Request.Path.ToString().ToLower();
            if (_options.Endpoints.Any(e => path.StartsWith(e)))
            {
                var match = Regex.Match(path, "^(?<identity>\\/[\\w]+)(?<action>\\/[\\w]+)");
                if (match.Success)
                {
                    _resolver = context.RequestServices.GetRequiredService<IStorageResolver>();
                    using (_resolver)
                    {
                        var identity = match.Groups["identity"].ToString();
                        var action = match.Groups["action"].ToString();

                        await _resolver.TryResolveAsync(identity);
                        if (_resolver.IsResolved)
                        {
                            switch (action)
                            {
                                case "/exists":
                                    await InvokeExistsAsync(context);
                                    break;
                                case "/init":
                                    await InvokeInitAsync(context);
                                    break;
                                case "/upload":
                                    await InvokeUploadAsync(context);
                                    break;
                                case "/complete":
                                    await InvokeCompleteAsync(context);
                                    break;
                                case "/failure":
                                    await InvokeFailureAsync(context);
                                    break;
                                case "/file":
                                    await InvokeDownloadAsync(context);
                                    break;
                                default:
                                    if (_options.RewritePath && context.Request.Path.StartsWithSegments(identity, out var matched, out var newPath))
                                    {
                                        context.Request.PathBase = context.Request.PathBase.Add(matched);
                                        context.Request.Path = newPath;
                                    }
                                    await _next(context);
                                    break;
                            }
                        }
                        else
                        {
                            await _next(context);
                        }
                    }
                }
                else
                {
                    await _next(context);
                }
            }
            else
            {
                if (path.StartsWith("/testthrow"))
                {
                    var storage = context.RequestServices.GetRequiredService<IStorage>();
                }
                await _next(context);
            }
        }

        #region Check file exists
        private string GetFilePathFromForm(HttpContext context)
        {
            var filePath = context.Request.Form["filePath"].ToString();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is missing in form");
            }
            return filePath;
        }

        private async Task InvokeExistsAsync(HttpContext context)
        {
            if (context.Request.Method != HttpMethod.Post.Method)
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }
            try
            {
                var uploadManager = context.RequestServices.GetRequiredService<IUploadManager>();

                var filePath = GetFilePathFromForm(context);

                var exists = await uploadManager.ExistsAsync(filePath, context.RequestAborted);

                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync(JsonConvert.SerializeObject(exists));
            }
            catch (ArgumentException ex)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(ex.Message);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        #endregion

        #region Init upload
        private InitialFileInfo GetInitialFileInfoFromForm(HttpContext context)
        {

            var filePath = context.Request.Form["filePath"].ToString();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is missing in form");
            }

            var originalFilePath = context.Request.Form["originalFilePath"].ToString();

            var fileSizeStr = context.Request.Form["fileSize"];
            if (string.IsNullOrWhiteSpace(fileSizeStr) || !long.TryParse(fileSizeStr, out var fileSize))
            {
                throw new ArgumentException("fileSize is missing in form");
            }

            var fileExistsBehaviorStr = context.Request.Form["fileExistsBehavior"];
            if (string.IsNullOrWhiteSpace(fileExistsBehaviorStr)
                || !Enum.TryParse<FileExistsBehavior>(fileExistsBehaviorStr, out var fileExistsBehavior))
            {
                throw new ArgumentException("fileExistsBehavior is missing in form");
            }

            DateTimeOffset? lastModified = null;
            var lastModifiedDate = context.Request.Form["lastModifiedDate"].ToString();
            if (!string.IsNullOrWhiteSpace(lastModifiedDate))
            {
                if (DateTimeOffset.TryParse(lastModifiedDate, out var tmp))
                {
                    lastModified = tmp;
                }
                else
                {
                    throw new ArgumentException("lastModifiedDate is invalid format. Try to convert to ISO format like this '2021-01-18T17:08:50.327+07:00'.");
                }
            }


            var contentType = context.Request.Form.ContainsKey("contentType") ?
                context.Request.Form["contentType"].ToString() : null;

            var correlationId = context.Request.Form.ContainsKey("correlationId") ?
                context.Request.Form["correlationId"].ToString() : null;
            var metadata = context.Request.Form.ContainsKey("metadata") ?
                context.Request.Form["metadata"].ToString() : null;

            JObject? metadataObj;
            try
            {
                metadataObj = !string.IsNullOrEmpty(metadata)
                   ? JsonConvert.DeserializeObject<JObject>(metadata)
                   : null;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("metadata is not valid json", ex);
            }

            var uploadIdStr = context.Request.Form["uploadId"];
            if (!string.IsNullOrWhiteSpace(fileSizeStr) && Guid.TryParse(uploadIdStr, out var uploadId))
            {
                return new InitialFileInfo(filePath, fileSize, contentType, originalFilePath, lastModified, correlationId,
                    metadataObj, fileExistsBehavior, uploadId);
            }

            return new InitialFileInfo(filePath, fileSize, contentType, originalFilePath, lastModified, correlationId,
                    metadataObj, fileExistsBehavior);

        }

        private async Task InvokeInitAsync(HttpContext context)
        {
            if (context.Request.Method != HttpMethod.Post.Method)
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }
            try
            {
                var uploadManager = context.RequestServices.GetRequiredService<IUploadManager>();

                var fileInfo = GetInitialFileInfoFromForm(context);

                var logger = context.RequestServices.GetRequiredService<ILogger<StorageMiddleware>>();
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Init upload {filePath} {fileSize} {contentType} {originalFilePath} {lastModified} {correlationId} {metadata} {fileExistsBehavior}",
                                               fileInfo.Name, fileInfo.FileSize, fileInfo.ContentType, fileInfo.OriginalName, fileInfo.LastModified, fileInfo.CorrelationId, fileInfo.Metadata, fileInfo.FileExistsBehavior);
                }

                var configuration = await uploadManager.InitAsync(fileInfo, context.RequestAborted);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(configuration, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

            }
            catch (ArgumentException ex)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync(ex.Message);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.Message);
            }
        }
        #endregion

        #region Upload

        private (Guid? uploadId, long offset) GetUploadInfoFromHeaders(HttpContext context)
        {
            var uploadIdStr = context.Request.Headers["x-uploadid"];
            var uploadId =
                string.IsNullOrWhiteSpace(uploadIdStr) || !Guid.TryParse(uploadIdStr, out var guid)
                ? (Guid?)null : guid;

            var offsetStr = context.Request.Headers["x-offset"];
            if (string.IsNullOrEmpty(offsetStr))
            {
                return (uploadId, default);
            }
            if (!long.TryParse(offsetStr, out var offset))
            {
                throw new ArgumentException("x-offset header is invalid");
            }
            return (uploadId, offset);
        }
        private InitialFileInfo GetInitialFileInfoFromFormData(Dictionary<string, string> form)
        {

            var filePath = form["filePath"].ToString();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is missing in form");
            }

            var originalFilePath = form["originalFilePath"].ToString();

            var fileSizeStr = form["fileSize"];
            if (string.IsNullOrWhiteSpace(fileSizeStr) || !long.TryParse(fileSizeStr, out var fileSize))
            {
                throw new ArgumentException("fileSize is missing in form");
            }

            var fileExistsBehaviorStr = form["fileExistsBehavior"];
            if (string.IsNullOrWhiteSpace(fileExistsBehaviorStr)
                || !Enum.TryParse<FileExistsBehavior>(fileExistsBehaviorStr, out var fileExistsBehavior))
            {
                throw new ArgumentException("fileExistsBehavior is missing in form");
            }

            DateTimeOffset? lastModified = null;
            var lastModifiedDate = form["lastModifiedDate"].ToString();
            if (!string.IsNullOrWhiteSpace(lastModifiedDate))
            {
                if (DateTimeOffset.TryParse(lastModifiedDate, out var tmp))
                {
                    lastModified = tmp;
                }
                else
                {
                    throw new ArgumentException("lastModifiedDate is invalid format. Try to convert to ISO format like this '2021-01-18T17:08:50.327+07:00'.");
                }
            }

            var contentType = form.ContainsKey("contentType") ?
                form["contentType"].ToString() : null;

            var correlationId = form.ContainsKey("correlationId") ?
                form["correlationId"].ToString() : null;
            var metadata = form.ContainsKey("metadata") ?
                form["metadata"].ToString() : null;

            JObject? metadataObj;
            try
            {
                metadataObj = !string.IsNullOrEmpty(metadata)
                   ? JsonConvert.DeserializeObject<JObject>(metadata)
                   : null;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("metadata is not valid json", ex);
            }

            return new InitialFileInfo(filePath, fileSize, contentType, originalFilePath, lastModified, correlationId,
                    metadataObj, fileExistsBehavior);
        }

        private async Task InvokeUploadAsync(HttpContext context)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<StorageMiddleware>>();
            var request = context.Request;

            var boundary = MultipartRequestHelper.GetBoundary(request);

            // validation of Content-Type
            // 1. first, it must be a form-data request
            // 2. a boundary should be found in the Content-Type

            if (!request.HasFormContentType || string.IsNullOrEmpty(boundary))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("No files data in the request");
            }

            try
            {
                var uploadManager = context.RequestServices.GetRequiredService<IUploadManager>();

                var (uploadId, offset) = GetUploadInfoFromHeaders(context);
                if (uploadId.HasValue)
                {
                    logger?.LogInformation("Upload file {uploadId} from offset {offset}", uploadId, offset);
                }
                else
                {
                    logger?.LogInformation("Upload new file from offset {offset}", offset);
                }
                var sectionSize = context.RequestServices.GetRequiredService<IOptionsSnapshot<UploadOptions>>().Value.SectionSize;

                var reader = new MultipartReader(boundary, context.Request.Body, 1024 * 1024)
                ;
                var section = await reader.ReadNextSectionAsync();

                var multipartFormData = new Dictionary<string, string>();
                Stream? stream = null;

                while (section != null)
                {
                    if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition,
                    out var contentDisposition) && contentDisposition.DispositionType.Equals("form-data")
                    && !string.IsNullOrEmpty(contentDisposition.FileName.Value)
                    )
                    {
                        UploadConfiguration? configuration = null;

                        if (!uploadId.HasValue)
                        {
                            var fileInfo = GetInitialFileInfoFromFormData(multipartFormData);
                            if (logger?.IsEnabled(LogLevel.Debug) ?? false)
                            {
                                logger.LogDebug("Init upload {filePath} {fileSize} {contentType} {originalFilePath} {lastModified} {correlationId} {metadata} {fileExistsBehavior}",
                                                           fileInfo.Name, fileInfo.FileSize, fileInfo.ContentType, fileInfo.OriginalName, fileInfo.LastModified, fileInfo.CorrelationId, fileInfo.Metadata, fileInfo.FileExistsBehavior);
                            }
                            configuration = await uploadManager.InitAsync(fileInfo, context.RequestAborted);
                            uploadId = configuration.UploadId;
                        }

                        stream = section.Body;
                        var (completed, preserved, size) = await uploadManager.UploadAsync(uploadId.Value, stream, offset, context.RequestAborted);
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        context.Response.Headers.Append("x-offset", size.ToString());
                        context.Response.Headers.Append("x-completed", completed.ToString());
                        context.Response.Headers.Append("x-date-modified-preserved", preserved.ToString());
                        if (configuration != null)
                        {
                            await context.Response.WriteAsJsonAsync(configuration, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                            });
                        }
                        return;
                    }
                    else
                    {
                        var x = section.AsFormDataSection();
                        if (x != null)
                        {
                            multipartFormData.Add(x.Name, await x.GetValueAsync());
                        }
                    }
                    section = await reader.ReadNextSectionAsync();
                }
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            catch (BadHttpRequestException ex)
            {
                if (ex.Message.StartsWith("Request body too large"))
                {
                    // try to read the limitation from the message
                    var match = Regex.Match(ex.Message, @"(\d+)");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var limit))
                    {
                        UploadOptions.ServerMaxBodySizeFromHandledError = limit;
                        context.Response.Headers.Append("x-upload-limit", limit.ToString());
                    }
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace(ex, ex.StackTrace);
                    }
                    await context.Response.WriteAsync(ex.Message);
                }
            }
            catch (IOException ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Failed to write file to storage");
                logger.LogError(ex, ex.Message);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogInformation("Client aborted upload");
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace(ex, ex.Message);
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.Message);
                logger.LogError(ex, ex.Message);
            }
        }

        #endregion

        #region Completed

        private Guid GetUploadIdFromForm(HttpContext context)
        {
            var uploadIdStr = context.Request.Form["uploadId"];
            if (string.IsNullOrWhiteSpace(uploadIdStr) || !Guid.TryParse(uploadIdStr, out var uploadId))
            {
                throw new ArgumentException("uploadId is missing in the form data");
            }
            return uploadId;
        }

        private async Task InvokeCompleteAsync(HttpContext context)
        {
            try
            {
                var logger = context.RequestServices.GetService<ILogger<StorageMiddleware>>();
                var uploadId = GetUploadIdFromForm(context);
                logger?.LogInformation("Upload completed {uploadId}", uploadId);

                var uploadManager = context.RequestServices.GetRequiredService<IUploadManager>();

                var preserved = await uploadManager.CompleteAsync(uploadId, context.RequestAborted);

                context.Response.Headers.Append("x-date-modified-preserved", preserved.ToString());

                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        private async Task InvokeFailureAsync(HttpContext context)
        {
            try
            {
                var logger = context.RequestServices.GetService<ILogger<StorageMiddleware>>();
                var uploadId = GetUploadIdFromForm(context);
                logger?.LogInformation("Upload failured {uploadId}", uploadId);

                var uploadManager = context.RequestServices.GetRequiredService<IUploadManager>();

                await uploadManager.FailureAsync(uploadId, context.RequestAborted);

                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.Message);
            }
        }
        #endregion

        #region Download

        private async Task InvokeDownloadAsync(HttpContext context)
        {
            if (context.Request.Method != HttpMethod.Get.Method)
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }
            try
            {
                if (_resolver == null || !_resolver.IsResolved)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Storage is not resolved");
                    return;
                }
                var path = _resolver.Identity + "/file";

                var filePath = context.Request.Path.ToString().Substring(path.Length).TrimStart('/');

                if (string.IsNullOrEmpty(filePath))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("File is missing in the request path");
                    return;
                }

                var downloadManager = context.RequestServices.GetService<IDownloadManager>();
                if (downloadManager == null)
                {
                    context.Response.StatusCode = StatusCodes.Status501NotImplemented;
                    await context.Response.WriteAsync("Download does not supported");
                    return;
                }

                var fileId = filePath.Split('/').First();

                Stream? stream = null;
                string? fileName = null;
                IOperationResult state;

                if (Guid.TryParse(fileId, out var id))
                {
                    var rs = await downloadManager.GetStreamAsync(id, context.RequestAborted);
                    state = rs;
                    fileName = context.Request.Query["fileName"].ToString();
                    if (string.IsNullOrEmpty(filePath))
                    {
                        if (string.IsNullOrEmpty(fileName))
                        {
                            fileName = Path.GetFileName(rs.DataValue.FileName);
                        }
                    }
                    stream = rs.Data.Stream;
                }
                else
                {
                    // if fileId is not a Guid, it is a file path
                    var rs = await downloadManager.GetStreamAsync(filePath, context.RequestAborted);
                    state = rs;
                    fileName = Path.GetFileName(filePath);
                    stream = rs.Data;
                }

                if (!state.Succeeded)
                {
                    context.Response.StatusCode = state.IsUnauthorized() ? StatusCodes.Status403Forbidden
                        : state.IsNotFound() ? StatusCodes.Status404NotFound
                        : StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync(state.ToString());
                    return;
                }
                if (stream == null || stream.Length == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("File not found or empty");
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/octet-stream";
                context.Response.Headers.Append("Accept-Ranges", "bytes");

                using (stream)
                {
                    var totalLength = stream.Length;
                    // support for range requests
                    if (context.Request.Headers.ContainsKey("Range"))
                    {
                        // Example: "Range: bytes=1000-"
                        var rangeHeader = context.Request.Headers["Range"].ToString();

                        if (RangeHeaderValue.TryParse(rangeHeader, out var rangeHeaderValue) &&
                            rangeHeaderValue.Ranges.FirstOrDefault() is RangeItemHeaderValue range)
                        {
                            long start = range.From ?? 0;
                            long end = range.To ?? (totalLength - 1);

                            if (start >= totalLength || end >= totalLength || start > end)
                            {
                                context.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                                context.Response.Headers["Content-Range"] = $"bytes */{totalLength}";
                                return;
                            }

                            long contentLength = end - start + 1;

                            context.Response.StatusCode = StatusCodes.Status206PartialContent;
                            context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{totalLength}";
                            context.Response.ContentLength = contentLength;

                            stream.Seek(start, SeekOrigin.Begin);
                            await stream.CopyToAsync(context.Response.Body, (int)contentLength, context.RequestAborted);
                            return;
                        }
                    }
                    else
                    {
                        context.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                    }

                    // if no range is specified, send the whole file
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentLength = totalLength;

                    await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (ArgumentException ex)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(ex.Message);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync(ex.Message);
            }
        }

        #endregion
    }

}
