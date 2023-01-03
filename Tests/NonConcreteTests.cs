using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Tests;

public enum EmployeeType
{
    // Employee,
    Manager,
    Contractor,
}

// [JsonDiscriminatorFallback]
public /*abstract*/ partial record Employee(
    string Name, 
    DateOnly DateJoined,
    [JsonDiscriminator] EmployeeType Type
);

public record Manager(string Name, DateOnly DateJoined)
    : Employee(Name, DateJoined, Type: EmployeeType.Manager)
{
    public List<string>? Reports { get; set; }
}

public record Contractor(string Name, DateOnly DateJoined)
    : Employee(Name, DateJoined, Type: EmployeeType.Contractor)
{
    public string? Company { get; set; }
}

public class NonConcreteTests
{
    [Fact]
    public void TestConcreteTypes()
    {
        var employees = new List<Employee>
        {
            new Manager("John", new DateOnly(2020, 1, 1))
            {
                Reports = new List<string> { "Jane", "Joe" },
            },
            new Contractor("Jane", new DateOnly(2020, 1, 1)),
            // new Employee("Joe", new DateOnly(2020, 1, 1)),
        };

        var serialized = JsonSerializer.Serialize(employees);

        // Deserialize
        var employeesDeserialized = JsonSerializer.Deserialize<List<Employee>>(serialized)!;

        // Assert
        Assert.Equal(employees.Count, employeesDeserialized.Count);

        for (var i = 0; i < employees.Count; ++i)
            Assert.Equal(employees[i].Name, employeesDeserialized[i].Name);
    }

    // [Fact]
    public void TestGenericTypes()
    {
        var employees = new List<Manager>
        {
            new Manager("John", new DateOnly(2020, 1, 1))
            {
                Reports = new List<string> { "Jane", "Joe" },
            },
            new Manager("Jane", new DateOnly(2020, 1, 1))
            {
                Reports = new List<string> { "Joe" },
            },
        };

        TestGeneric(employees);

        void TestGeneric<T>(List<T> employees)
            where T : Employee
        {
            var serialized = JsonSerializer.Serialize(employees.Cast<Employee>().ToList());

            // Deserialize
            var employeesDeserialized = JsonSerializer.Deserialize<List<T>>(serialized)!;

            // Assert
            Assert.Equal(employees.Count, employeesDeserialized.Count);

            for (var i = 0; i < employees.Count; ++i)
                Assert.Equal(employees[i].Name, employeesDeserialized[i].Name);
        }
    }
}