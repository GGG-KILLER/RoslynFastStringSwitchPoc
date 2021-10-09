bool IsKnownContentType(string contentTypeValue)
{
    {
        var __switchExpr__0 = contentTypeValue;
        string? __candidate__1 = null;
        int __destination__2 = -1;
        switch (__switchExpr__0.Length)
        {
            case 8:
                switch (__switchExpr__0[7])
                {
                    case 'l':
                        __candidate__1 = "text/xml";
                        __destination__2 = 0;
                        break;
                    case 's':
                        __candidate__1 = "text/css";
                        __destination__2 = 0;
                        break;
                    case 'v':
                        __candidate__1 = "text/csv";
                        __destination__2 = 0;
                        break;
                }

                break;
            case 9:
                switch (__switchExpr__0[6])
                {
                    case 'g':
                        __candidate__1 = "image/gif";
                        __destination__2 = 0;
                        break;
                    case 'p':
                        __candidate__1 = "image/png";
                        __destination__2 = 0;
                        break;
                    case 't':
                        __candidate__1 = "text/html";
                        __destination__2 = 0;
                        break;
                }

                break;
            case 10:
                switch (__switchExpr__0[0])
                {
                    case 't':
                        __candidate__1 = "text/plain";
                        __destination__2 = 0;
                        break;
                    case 'i':
                        __candidate__1 = "image/jpeg";
                        __destination__2 = 0;
                        break;
                }

                break;
            case 15:
                switch (__switchExpr__0[12])
                {
                    case 'p':
                        __candidate__1 = "application/pdf";
                        __destination__2 = 0;
                        break;
                    case 'x':
                        __candidate__1 = "application/xml";
                        __destination__2 = 0;
                        break;
                    case 'z':
                        __candidate__1 = "application/zip";
                        __destination__2 = 0;
                        break;
                }

                break;
            case 16:
                switch (__switchExpr__0[12])
                {
                    case 'g':
                        __candidate__1 = "application/grpc";
                        __destination__2 = 0;
                        break;
                    case 'j':
                        __candidate__1 = "application/json";
                        __destination__2 = 0;
                        break;
                }

                break;
            case 19:
                __candidate__1 = "multipart/form-data";
                __destination__2 = 0;
                break;
            case 22:
                __candidate__1 = "application/javascript";
                __destination__2 = 0;
                break;
            case 24:
                switch (__switchExpr__0[0])
                {
                    case 'a':
                        __candidate__1 = "application/octet-stream";
                        __destination__2 = 0;
                        break;
                    case 't':
                        __candidate__1 = "text/html; charset=utf-8";
                        __destination__2 = 0;
                        break;
                }

                break;
            case 25:
                __candidate__1 = "text/plain; charset=utf-8";
                __destination__2 = 0;
                break;
            case 31:
                __candidate__1 = "application/json; charset=utf-8";
                __destination__2 = 0;
                break;
            case 33:
                __candidate__1 = "application/x-www-form-urlencoded";
                __destination__2 = 0;
                break;
        }

        if (__destination__2 != -1 && (object.ReferenceEquals(__switchExpr__0, __candidate__1) || __switchExpr__0?.Equals(__candidate__1) is true))
        {
            switch (__destination__2)
            {
                case 0:
                    return true;
                default:
                    System.Diagnostics.Debug.Fail("Default case was hit.");
                    break;
            }
        }
    }

    return false;
}