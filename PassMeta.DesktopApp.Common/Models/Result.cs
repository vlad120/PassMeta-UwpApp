namespace PassMeta.DesktopApp.Common.Models
{
    /// <summary>
    /// Common result model.
    /// </summary>
    public readonly struct Result
    {
        /// <summary>
        /// Result is success.
        /// </summary>
        public readonly bool Ok;
        
        /// <summary>
        /// Result optional message;
        /// </summary>
        public readonly string? Message;

        /// <summary>
        /// Not <see cref="Ok"/>.
        /// </summary>
        public bool Bad => !Ok;
        
        internal Result(bool ok, string? message = null)
        {
            Ok = ok;
            Message = message;
        }

        /// <summary>
        /// Cast to <see cref="Result{TData}"/> with null data.
        /// </summary>
        public Result<TData> WithNullData<TData>() => new(Ok, Message);
        
        /// <summary>
        /// Make success result with optional message.
        /// </summary>
        public static Result Success(string? message = null) => new(true, message);
        
        /// <summary>
        /// Make failure result with optional message.
        /// </summary>
        public static Result Failure(string? message = null) => new(false, message);
        
        /// <summary>
        /// Make success result with data and optional message.
        /// </summary>
        public static Result<TData> Success<TData>(TData data, string? message = null) => new(data, message);
        
        /// <summary>
        /// Make failure data result with optional message.
        /// </summary>
        public static Result<TData> Failure<TData>(string? message = null) => new(false, message);
        
        /// <summary>
        /// Make success/failure result from response.
        /// </summary>
        public static Result FromResponse(OkBadResponse? response) => response?.Success is true
            ? new Result()
            : new Result(false, response?.Message);
        
        /// <summary>
        /// Make success/failure result from response.
        /// </summary>
        public static Result<TData> FromResponse<TData>(OkBadResponse<TData>? response) => response?.Success is true
            ? new Result<TData>(response.Data!)
            : new Result<TData>(false, response?.Message);
        
        /// <summary>
        /// Make success/failure result depending on boolean <paramref name="ok"/> value.
        /// </summary>
        public static Result From(bool ok) => new(ok);
        
        /// <summary>
        /// Make success/failure data result depending on boolean <paramref name="ok"/> value.
        /// </summary>
        public static Result<TData> From<TData>(bool ok, TData data) where TData : notnull => ok
            ? new Result<TData>(data) 
            : new Result<TData>(false);
    }
    
    /// <summary>
    /// <see cref="Result"/> with optional data.
    /// </summary>
    public readonly struct Result<TData>
    {
        /// <summary>
        /// Result is success.
        /// </summary>
        public readonly bool Ok;
        
        /// <summary>
        /// Result optional message;
        /// </summary>
        public readonly string? Message;
        
        /// <summary>
        /// Result optional data.
        /// </summary>
        public readonly TData? Data;
        
        /// <summary>
        /// Not <see cref="Ok"/>.
        /// </summary>
        public bool Bad => !Ok;

        /// <summary>
        /// Deconstruct to <see cref="Ok"/> and <see cref="Data"/>.
        /// </summary>
        public void Deconstruct(out bool ok, out TData? data)
        {
            ok = Ok;
            data = Data;
        }

        internal Result(TData data, string? message = null)
        {
            Ok = true;
            Message = message;
            Data = data;
        }

        internal Result(bool ok, string? message = null)
        {
            Ok = ok;
            Message = message;
            Data = default;
        }
    }
}