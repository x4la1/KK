using System;

namespace MarketplaceDomain
{
    public enum OrderStatus
    {
        Created,
        Paid,
        Delivered,
        Cancelled
    }

    public interface IPaymentGateway
    {
        bool Charge(Guid orderId, decimal amount);
        void Refund(Guid orderId, decimal amount);
    }

    public interface ILoyaltyService
    {
        int GetUserBalance(int userId);
        void WriteOffPoints(int userId, int points);
    }

    public interface INotificationSender
    {
        void SendStatusUpdate(int userId, string message);
    }

    public class MarketplaceOrder
    {
        private readonly IPaymentGateway _paymentGateway;
        private readonly ILoyaltyService _loyaltyService;
        private readonly INotificationSender _notificationSender;

        public Guid Id { get; }
        public int CustomerId { get; }
        public decimal TotalPrice { get; private set; }
        public int AppliedBonusPoints { get; private set; }
        public int DeliveryAttempts { get; private set; }
        public OrderStatus Status { get; private set; }

        public MarketplaceOrder(
            Guid id,
            int customerId,
            decimal initialPrice,
            IPaymentGateway paymentGateway,
            ILoyaltyService loyaltyService,
            INotificationSender notificationSender)
        {
            if (initialPrice <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialPrice), "Initial price must be positive.");
            }

            Id = id;
            CustomerId = customerId;
            TotalPrice = initialPrice;
            Status = OrderStatus.Created;

            _paymentGateway = paymentGateway;
            _loyaltyService = loyaltyService;
            _notificationSender = notificationSender;
        }

        public void ApplyDiscount(decimal discountPercent)
        {
            if (Status != OrderStatus.Created)
            {
                throw new InvalidOperationException("Discount can apply only to created orders.");
            }

            if (discountPercent <= 0 || discountPercent > 50)
            {
                throw new ArgumentOutOfRangeException(nameof(discountPercent), "Discount must be between 1 and 50%.");
            }

            decimal discountAmount = Math.Round(TotalPrice * (discountPercent / 100m), 2);
            TotalPrice -= discountAmount;
        }

        public void ApplyLoyaltyPoints(int points)
        {
            if (Status != OrderStatus.Created)
            {
                throw new InvalidOperationException("Loyalty points can be applied only to created orders.");
            }

            if (points <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "Points must be positive.");
            }

            int availableBalance = _loyaltyService.GetUserBalance(CustomerId);
            if (availableBalance < points)
            {
                throw new InvalidOperationException("Insufficient loyalty points.");
            }

            int maxAllowedPoints = (int)Math.Floor(TotalPrice * 0.30m);
            int pointsToDeduct = Math.Min(points, maxAllowedPoints);

            _loyaltyService.WriteOffPoints(CustomerId, pointsToDeduct);
            AppliedBonusPoints += pointsToDeduct;
            TotalPrice -= pointsToDeduct;
        }

        public bool Pay()
        {
            if (Status != OrderStatus.Created)
            {
                throw new InvalidOperationException("Payment is available only for created orders.");
            }

            bool isCharged = _paymentGateway.Charge(Id, TotalPrice);
            if (!isCharged)
            {
                _notificationSender.SendStatusUpdate(CustomerId, "Payment error.");
                return false;
            }

            Status = OrderStatus.Paid;
            _notificationSender.SendStatusUpdate(CustomerId, "Order successfully paid.");
            return true;
        }

        public void ProcessDeliveryAttempt(bool isHandedToCustomer)
        {
            if (Status != OrderStatus.Paid)
            {
                throw new InvalidOperationException("Delivery is available only for paid orders.");
            }

            DeliveryAttempts++;

            if (isHandedToCustomer)
            {
                Status = OrderStatus.Delivered;
                _notificationSender.SendStatusUpdate(CustomerId, "Order successfully delivered.");
                return;
            }

            if (DeliveryAttempts >= 3)
            {
                Cancel("Exceeded delivery attempt limit.");
            }
        }

        public void Cancel(string reason)
        {
            if (Status == OrderStatus.Delivered)
            {
                throw new InvalidOperationException("Delivered orders cannot be cancelled.");
            }

            if (Status == OrderStatus.Cancelled)
            {
                throw new InvalidOperationException("Order is already cancelled.");
            }

            if (Status == OrderStatus.Paid)
            {
                _paymentGateway.Refund(Id, TotalPrice);
            }

            Status = OrderStatus.Cancelled;
            _notificationSender.SendStatusUpdate(CustomerId, $"Order cancelled: {reason}");
        }
    }
}