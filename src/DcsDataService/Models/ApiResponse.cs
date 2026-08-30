namespace DcsDataService.Models
{
    public sealed class ApiResponse
    {
        public bool ok { get; set; } public object data { get; set; } public ApiError error { get; set; }
        public static ApiResponse Success(object value) { return new ApiResponse { ok = true, data = value }; }
        public static ApiResponse Failure(string code, string message) { return new ApiResponse { ok = false, error = new ApiError { code = code, message = message } }; }
    }
}
