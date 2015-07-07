﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LINQtoCSV;
using NUnit.Framework;
using TrueShipAccess;
using TrueShipAccess.Models;

namespace TrueShipAccessTests.Orders
{
	public class TestConfig
	{
		public int CompanyId { get; set; }
		public string AccessToken { get; set; }
	}

	public class ExistingOrderIds
	{
		public static readonly List<string> OrderIds = new List<string> { "TRUE000001", "TRUE000002", "TRUE000003" };
		public static readonly List<string> BoxIds = new List<string>
		{
			"/api/v1/items/7747246/",
			"/api/v1/items/7747245/",
			"/api/v1/items/7747244/",
			"/api/v1/items/5203283/",
			"/api/v1/items/5203282/",
			"/api/v1/items/5203281/",
			"/api/v1/items/5203221/",
			"/api/v1/items/5203220/",
			"/api/v1/items/5203219/"
		};
	}

	[TestFixture]
	public class OrdersTests
	{
		private ITrueShipFactory _factory;
		public TrueShipConfiguration Config { get; set; }
		public TrueShipCredentials Credentials { get; set; }

		[SetUp]
		public void Init()
		{
			const string credentialsFilePath = @"..\..\Files\TrueShipCredentials.csv";

			var cc = new CsvContext();
			var testConfig =
				cc.Read<TestConfig>(credentialsFilePath, new CsvFileDescription { FirstLineHasColumnNames = true, SeparatorChar = ';' }).FirstOrDefault();

			if (testConfig != null)
			{
				this.Config = new TrueShipConfiguration(DateTime.MinValue, DateTime.MinValue);
				this.Credentials = new TrueShipCredentials(testConfig.CompanyId, testConfig.AccessToken);

				this._factory = new TrueShipFactory(this.Config);
			}
		}

		[Test]
		public void GetOrders()
		{
			//------------ Arrange
			var service = _factory.CreateService(this.Credentials);

			//------------ Act
			var orders = service.GetOrdersAsync(this.Config.LastOrderSync, DateTime.MaxValue);
			orders.Wait();

			//------------ Assert
			Assert.IsNotNull(orders);
			orders.Result.Should().NotBeEmpty();
			CollectionAssert.AreEquivalent(ExistingOrderIds.OrderIds, orders.Result.Select(x => x.PrimaryId));
		}

		[Test]
		public async Task CanUpdateOrderPickLocation()
		{
			//------------ Arrange
			var service = _factory.CreateService(this.Credentials);

			//------------ Act
			var wasUpdated = await service.UpdateOrderItemPickLocations(new List<KeyValuePair<string, PickLocation>>
			{
				new KeyValuePair<string, PickLocation>(ExistingOrderIds.BoxIds.First(), new PickLocation
				{
					pick_location = "Somwhere1"
				})
			});


			//------------ Assert
			Assert.IsTrue(wasUpdated);
		}

		[Test]
		public void CanGetBoxes()
		{
			//------------ Arrange
			var service = _factory.CreateService(this.Credentials);

			//------------ Act
			var boxes = service.GetBoxes(10, 0);
			boxes.Wait();

			//------------ Assert
			boxes.Result.Should().NotBeEmpty();
		}
	}
}