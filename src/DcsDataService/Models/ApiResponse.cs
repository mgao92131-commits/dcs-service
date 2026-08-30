namespace DcsDataService.Models
{
    public sealed class ApiResponse
    {
        public bool Ok { get; set; } public object Data { get; set; } public ApiError Error { get; set; }
        public static ApiResponse Success(object data) { return new ApiResponse { Ok = true, Data = data }; }
        public static ApiResponse Failure(string code, string message) { return new ApiResponse { Ok = false, Error = new ApiError { Code = code, Message = message } }; }
    }
}
