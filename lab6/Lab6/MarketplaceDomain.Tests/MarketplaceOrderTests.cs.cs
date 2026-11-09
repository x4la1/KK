using System;
using MarketplaceDomain;
using Moq;
using NUnit.Framework;

namespace MarketplaceDomain.Tests
{
    [TestFixture]
    public class MarketplaceOrderTests
    {
        private readonly Guid _orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private const int CustomerId = 42;
        private const decimal InitialPrice = 1000m;

        private Mock<IPaymentGateway> _paymentMock;
        private Mock<ILoyaltyService> _loyaltyMock;
        private Mock<INotificationSender> _notificationMock;
        private MarketplaceOrder _order;

        [SetUp]
        public void SetUp()
        {
            _paymentMock = new Mock<IPaymentGateway>();
            _loyaltyMock = new Mock<ILoyaltyService>();
            _notificationMock = new Mock<INotificationSender>();

            _order = new MarketplaceOrder(
                _orderId,
                CustomerId,
                InitialPrice,
                _paymentMock.Object,
                _loyaltyMock.Object,
                _notificationMock.Object);
        }

        [Test]
        public void ConstructorCreatesObject()
        {
            MarketplaceOrder marketPlace = new MarketplaceOrder(_orderId, CustomerId, 100m, _paymentMock.Object, _loyaltyMock.Object, _notificationMock.Object);

            Assert.That(marketPlace.TotalPrice, Is.EqualTo(100m));
            Assert.That(_order.Id, Is.EqualTo(_orderId));
            Assert.That(_order.CustomerId, Is.EqualTo(CustomerId));
        }

        [Test]
        public void ConstructorNonPositivePriceThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MarketplaceOrder(_orderId, CustomerId, 0m, _paymentMock.Object, _loyaltyMock.Object, _notificationMock.Object));
        }

        [Test]
        public void ApplyDiscountMutatesTotalPrice()
        {
            _order.ApplyDiscount(20m);

            Assert.That(_order.TotalPrice, Is.EqualTo(800m));
            Assert.That(_order.Id, Is.EqualTo(_orderId));
            Assert.That(_order.CustomerId, Is.EqualTo(CustomerId));
        }

        [TestCase(0)]
        [TestCase(51)]
        public void ApplyInvalidDiscountThrowsArgumentOutOfRangeException(decimal invalidPercent)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _order.ApplyDiscount(invalidPercent));
        }

        [Test]
        public void ApplyDiscountToPaidOrderThrowsInvalidOperationException()
        {
            _paymentMock.Setup(p => p.Charge(_orderId, InitialPrice)).Returns(true);
            _order.Pay();

            Assert.Throws<InvalidOperationException>(() => _order.ApplyDiscount(10m));
        }

        [Test]
        public void ApplyLoyaltyPointsMoreThanCanApplyCalculateCorrect()
        {
            _loyaltyMock.Setup(l => l.GetUserBalance(CustomerId)).Returns(500);

            _order.ApplyLoyaltyPoints(400);

            Assert.That(_order.AppliedBonusPoints, Is.EqualTo(300));
            Assert.That(_order.TotalPrice, Is.EqualTo(700m));
            _loyaltyMock.Verify(l => l.WriteOffPoints(CustomerId, 300), Times.Once);
        }

        [Test]
        public void ApplyLoyaltyPointsInsufficientUserBalanceThrowsInvalidOperationException()
        {
            _loyaltyMock.Setup(l => l.GetUserBalance(CustomerId)).Returns(100);

            Assert.Throws<InvalidOperationException>(() => _order.ApplyLoyaltyPoints(200));
            _loyaltyMock.Verify(l => l.WriteOffPoints(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void ApplyLoyaltyPointsNonPositivePointsThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _order.ApplyLoyaltyPoints(0));
        }

        [Test]
        public void ApplyLoyaltyPointsWhenOrderNotCreatedThrowsInvalidOperationException()
        {
            _order.Cancel("Cancel");

            Assert.Throws<InvalidOperationException>(() => _order.ApplyLoyaltyPoints(100));
        }

        [Test]
        public void PayGatewayDeclinesKeepsCreatedStatusAndSendsFailureNotification()
        {
            _paymentMock.Setup(p => p.Charge(_orderId, InitialPrice)).Returns(false);

            bool result = _order.Pay();

            Assert.That(result, Is.False);
            Assert.That(_order.Status, Is.EqualTo(OrderStatus.Created));
            _notificationMock.Verify(n => n.SendStatusUpdate(CustomerId, "Payment error."), Times.Once);
        }

        [Test]
        public void PayWhenCancelledThrowsInvalidOperationException()
        {
            _order.Cancel("Cancel");

            Assert.Throws<InvalidOperationException>(() => _order.Pay());
        }

        [Test]
        public void ProcessDeliveryAttemptHandedToCustomerMutatesStatusToDelivered()
        {
            _paymentMock.Setup(p => p.Charge(_orderId, InitialPrice)).Returns(true);
            _order.Pay();

            _order.ProcessDeliveryAttempt(true);

            Assert.That(_order.DeliveryAttempts, Is.EqualTo(1));
            Assert.That(_order.Status, Is.EqualTo(OrderStatus.Delivered));
            _notificationMock.Verify(n => n.SendStatusUpdate(CustomerId, "Order successfully delivered."), Times.Once);
        }

        [Test]
        public void ProcessDeliveryAttemptThreeFailedAttemptsAutoCancelsAndRefundsPayment()
        {
            _paymentMock.Setup(p => p.Charge(_orderId, InitialPrice)).Returns(true);
            _order.Pay();

            _order.ProcessDeliveryAttempt(false);
            _order.ProcessDeliveryAttempt(false);
            _order.ProcessDeliveryAttempt(false);

            Assert.That(_order.DeliveryAttempts, Is.EqualTo(3));
            Assert.That(_order.Status, Is.EqualTo(OrderStatus.Cancelled));
            _paymentMock.Verify(p => p.Refund(_orderId, InitialPrice), Times.Once);
        }

        [Test]
        public void ProcessDeliveryAttemptWhenUnpaidThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => _order.ProcessDeliveryAttempt(true));
        }

        [Test]
        public void CancelWhenDeliveredThrowsInvalidOperationException()
        {
            _paymentMock.Setup(p => p.Charge(_orderId, InitialPrice)).Returns(true);
            _order.Pay();
            _order.ProcessDeliveryAttempt(true);

            Assert.Throws<InvalidOperationException>(() => _order.Cancel("Refund"));
        }

        [Test]
        public void CancelWhenCancelledThrowsInvalidOperationException()
        {
            _order.Cancel("First cancellation");

            Assert.Throws<InvalidOperationException>(() => _order.Cancel("Second cancellation"));
        }
    }
}