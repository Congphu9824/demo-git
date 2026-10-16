namespace ECommerce.Application.Exceptions;

public class CustomerNotFoundException : Exception
{
    public Guid CustomerId { get; }

    public CustomerNotFoundException(Guid customerId)
        : base($"Customer with ID '{customerId}' was not found or is inactive.")
    {
        CustomerId = customerId;
    }
}

public class ProductNotFoundException : Exception
{
    public Guid ProductId { get; }

    public ProductNotFoundException(Guid productId)
        : base($"Product with ID '{productId}' was not found.")
    {
        ProductId = productId;
    }
}

public class InsufficientStockException : Exception
{
    public Guid ProductId { get; }
    public string ProductName { get; }
    public int Requested { get; }
    public int Available { get; }

    public InsufficientStockException(Guid productId, string productName, int requested, int available)
        : base($"Insufficient stock for product '{productName}' ({productId}). Requested: {requested}, Available: {available}")
    {
        ProductId = productId;
        ProductName = productName;
        Requested = requested;
        Available = available;
    }
}

public class PaymentProcessingException : Exception
{
    public string TransactionId { get; }

    public PaymentProcessingException(string message, string transactionId, Exception? innerException = null)
        : base(message, innerException)
    {
        TransactionId = transactionId;
    }
}

public class DatabaseSimulatedException : Exception
{
    public DatabaseSimulatedException(string message) : base(message) { }
}

public class ExternalServiceException : Exception
{
    public string ServiceName { get; }

    public ExternalServiceException(string serviceName, string message, Exception? innerException = null)
        : base($"External service '{serviceName}' failed: {message}", innerException)
    {
        ServiceName = serviceName;
    }
}
