bool IsKnownContentType(string contentTypeValue)
{
    switch (contentTypeValue)
    {
        case "text/xml":
        case "text/css":
        case "text/csv":
        case "image/gif":
        case "image/png":
        case "text/html":
        case "text/plain":
        case "image/jpeg":
        case "application/pdf":
        case "application/xml":
        case "application/zip":
        case "application/grpc":
        case "application/json":
        case "multipart/form-data":
        case "application/javascript":
        case "application/octet-stream":
        case "text/html; charset=utf-8":
        case "text/plain; charset=utf-8":
        case "application/json; charset=utf-8":
        case "application/x-www-form-urlencoded":
            return true;
    }
    return false;
}
