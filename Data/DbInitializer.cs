using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Ensure database is created
            await context.Database.EnsureCreatedAsync();

            // Enforce database-level append-only triggers on SQLite engine for AuditLogs
            try
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    CREATE TRIGGER IF NOT EXISTS trg_AuditLogs_PreventUpdate
                    BEFORE UPDATE ON AuditLogs
                    BEGIN
                        SELECT RAISE(FAIL, 'Audit records are strictly append-only and cannot be modified.');
                    END;

                    CREATE TRIGGER IF NOT EXISTS trg_AuditLogs_PreventDelete
                    BEFORE DELETE ON AuditLogs
                    BEGIN
                        SELECT RAISE(FAIL, 'Audit records are strictly append-only and cannot be deleted.');
                    END;
                ");
            }
            catch
            {
                // Ignore if provider doesn't support or already exists
            }

            // 1. Seed Roles
            string[] roles = { "Admin", "Manager", "SalesExecutive" };
            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // 2. Seed Users
            var usersToCreate = new List<(string Email, string Name, string Role, string Password)>
            {
                ("admin@acxiomcrm.com", "System Administrator", "Admin", "Admin@12345"),
                ("manager@acxiomcrm.com", "Sarah Jenkins (Sales Manager)", "Manager", "Manager@12345"),
                ("sales1@acxiomcrm.com", "Alex Rivera (Senior Sales Rep)", "SalesExecutive", "Sales@12345"),
                ("sales2@acxiomcrm.com", "Priya Sharma (Sales Executive)", "SalesExecutive", "Sales@12345")
            };

            foreach (var item in usersToCreate)
            {
                var existing = await userManager.FindByEmailAsync(item.Email);
                if (existing == null)
                {
                    var user = new ApplicationUser
                    {
                        UserName = item.Email,
                        Email = item.Email,
                        FullName = item.Name,
                        EmailConfirmed = true,
                        IsActive = true,
                        PrimaryRole = item.Role,
                        CreatedDate = DateTime.UtcNow
                    };

                    var createResult = await userManager.CreateAsync(user, item.Password);
                    if (createResult.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, item.Role);
                    }
                }
            }

            var managerUser = await userManager.FindByEmailAsync("manager@acxiomcrm.com");
            var salesUser1 = await userManager.FindByEmailAsync("sales1@acxiomcrm.com");
            var salesUser2 = await userManager.FindByEmailAsync("sales2@acxiomcrm.com");
            var adminUser = await userManager.FindByEmailAsync("admin@acxiomcrm.com");

            // Assign Manager hierarchy (sales1 and sales2 belong to manager's team)
            if (managerUser != null)
            {
                if (salesUser1 != null && string.IsNullOrEmpty(salesUser1.ManagerId))
                {
                    salesUser1.ManagerId = managerUser.Id;
                    await userManager.UpdateAsync(salesUser1);
                }
                if (salesUser2 != null && string.IsNullOrEmpty(salesUser2.ManagerId))
                {
                    salesUser2.ManagerId = managerUser.Id;
                    await userManager.UpdateAsync(salesUser2);
                }
            }

            if (salesUser1 == null || adminUser == null) return;

            // 3. Seed Customers
            if (!context.Customers.Any())
            {
                var customers = new List<Customer>
                {
                    new Customer
                    {
                        CustomerCode = "CUST-1001",
                        CustomerName = "Apex Global Technologies",
                        Email = "contact@apextech.com",
                        Phone = "9876543210",
                        CompanyName = "Apex Global Inc.",
                        Address = "104 Tech Boulevard",
                        City = "Bangalore",
                        State = "Karnataka",
                        Status = "Active",
                        CreatedDate = DateTime.UtcNow.AddDays(-60),
                        CreatedBy = salesUser1.Id,
                        Notes = "Enterprise cloud client with 500+ seats."
                    },
                    new Customer
                    {
                        CustomerCode = "CUST-1002",
                        CustomerName = "BlueHorizon Logistics",
                        Email = "procure@bluehorizon.io",
                        Phone = "9123456780",
                        CompanyName = "BlueHorizon Pvt Ltd",
                        Address = "45 Maritime Way",
                        City = "Mumbai",
                        State = "Maharashtra",
                        Status = "Active",
                        CreatedDate = DateTime.UtcNow.AddDays(-45),
                        CreatedBy = salesUser1.Id,
                        Notes = "Fleet optimization solution contract signed."
                    },
                    new Customer
                    {
                        CustomerCode = "CUST-1003",
                        CustomerName = "Crestview Financials",
                        Email = "info@crestviewfin.com",
                        Phone = "9988776655",
                        CompanyName = "Crestview Holdings",
                        Address = "12 Finance Plaza",
                        City = "Delhi",
                        State = "Delhi",
                        Status = "Active",
                        CreatedDate = DateTime.UtcNow.AddDays(-20),
                        CreatedBy = salesUser2?.Id ?? salesUser1.Id,
                        Notes = "Evaluating AI fraud prevention module."
                    }
                };

                context.Customers.AddRange(customers);
                await context.SaveChangesAsync();
            }

            // 4. Seed Leads
            if (!context.Leads.Any())
            {
                var leads = new List<Lead>
                {
                    new Lead
                    {
                        LeadCode = "LEAD-2001",
                        LeadName = "David Miller",
                        Email = "david.miller@zenithhealth.org",
                        Phone = "9811223344",
                        CompanyName = "Zenith Healthcare",
                        Source = "Website",
                        Status = "Qualified",
                        Priority = "High",
                        ExpectedValue = 45000,
                        CreatedDate = DateTime.UtcNow.AddDays(-15),
                        AssignedTo = salesUser1.Id,
                        Notes = "Requested custom demo for hospital management integration."
                    },
                    new Lead
                    {
                        LeadCode = "LEAD-2002",
                        LeadName = "Sunita Rao",
                        Email = "sunita@omnicommerce.in",
                        Phone = "9822334455",
                        CompanyName = "OmniCommerce Retail",
                        Source = "Referral",
                        Status = "Contacted",
                        Priority = "Medium",
                        ExpectedValue = 28000,
                        CreatedDate = DateTime.UtcNow.AddDays(-10),
                        AssignedTo = salesUser2?.Id ?? salesUser1.Id,
                        Notes = "Initial introductory call completed. Follow up next week."
                    },
                    new Lead
                    {
                        LeadCode = "LEAD-2003",
                        LeadName = "Marcus Vance",
                        Email = "marcus@vancemedia.co",
                        Phone = "9833445566",
                        CompanyName = "Vance Media House",
                        Source = "Advertisement",
                        Status = "New",
                        Priority = "Low",
                        ExpectedValue = 15000,
                        CreatedDate = DateTime.UtcNow.AddDays(-2),
                        AssignedTo = salesUser1.Id,
                        Notes = "Inbound lead from Google ad campaign."
                    },
                    new Lead
                    {
                        LeadCode = "LEAD-2004",
                        LeadName = "Elena Petrova",
                        Email = "elena@nordicconsulting.com",
                        Phone = "9844556677",
                        CompanyName = "Nordic Analytics",
                        Source = "Partner",
                        Status = "Converted",
                        Priority = "High",
                        ExpectedValue = 60000,
                        CreatedDate = DateTime.UtcNow.AddDays(-30),
                        AssignedTo = salesUser1.Id,
                        Notes = "Successfully converted to client."
                    }
                };

                context.Leads.AddRange(leads);
                await context.SaveChangesAsync();
            }

            // 5. Seed Opportunities
            if (!context.Opportunities.Any())
            {
                var cust1 = context.Customers.FirstOrDefault(c => c.CustomerCode == "CUST-1001");
                var cust2 = context.Customers.FirstOrDefault(c => c.CustomerCode == "CUST-1002");
                var cust3 = context.Customers.FirstOrDefault(c => c.CustomerCode == "CUST-1003");

                var opps = new List<Opportunity>
                {
                    new Opportunity
                    {
                        OpportunityName = "Apex Cloud Migration Enterprise Suite",
                        CustomerId = cust1?.CustomerId,
                        Amount = 75000,
                        Stage = "Negotiation",
                        Probability = 80,
                        ExpectedCloseDate = DateTime.UtcNow.AddDays(20),
                        Status = "Open",
                        CreatedDate = DateTime.UtcNow.AddDays(-25),
                        AssignedTo = salesUser1.Id,
                        Notes = "Final pricing negotiation in progress with CTO."
                    },
                    new Opportunity
                    {
                        OpportunityName = "BlueHorizon Telematics Subscription",
                        CustomerId = cust2?.CustomerId,
                        Amount = 35000,
                        Stage = "Proposal",
                        Probability = 60,
                        ExpectedCloseDate = DateTime.UtcNow.AddDays(40),
                        Status = "Open",
                        CreatedDate = DateTime.UtcNow.AddDays(-14),
                        AssignedTo = salesUser1.Id,
                        Notes = "Proposal submitted. Awaiting board review."
                    },
                    new Opportunity
                    {
                        OpportunityName = "Crestview Security Audit & Compliance",
                        CustomerId = cust3?.CustomerId,
                        Amount = 50000,
                        Stage = "Qualification",
                        Probability = 30,
                        ExpectedCloseDate = DateTime.UtcNow.AddDays(60),
                        Status = "Open",
                        CreatedDate = DateTime.UtcNow.AddDays(-5),
                        AssignedTo = salesUser2?.Id ?? salesUser1.Id,
                        Notes = "Requirements gathering session scheduled."
                    },
                    new Opportunity
                    {
                        OpportunityName = "Apex Data Warehouse Expansion",
                        CustomerId = cust1?.CustomerId,
                        Amount = 90000,
                        Stage = "Won",
                        Probability = 100,
                        ExpectedCloseDate = DateTime.UtcNow.AddDays(-10),
                        Status = "Won",
                        CreatedDate = DateTime.UtcNow.AddDays(-60),
                        AssignedTo = salesUser1.Id,
                        Notes = "Deal closed successfully."
                    }
                };

                context.Opportunities.AddRange(opps);
                await context.SaveChangesAsync();
            }

            // 6. Seed Follow-Ups
            if (!context.FollowUps.Any())
            {
                var cust1 = context.Customers.FirstOrDefault();
                var lead1 = context.Leads.FirstOrDefault();
                var opp1 = context.Opportunities.FirstOrDefault();

                var followups = new List<FollowUp>
                {
                    new FollowUp
                    {
                        CustomerId = cust1?.CustomerId,
                        Subject = "Contract Terms Review Meeting",
                        FollowUpType = "Meeting",
                        FollowUpDate = DateTime.UtcNow.Date.AddDays(1),
                        Status = "Planned",
                        Remarks = "Discuss SLA clauses and payment milestones.",
                        AssignedTo = salesUser1.Id,
                        CreatedDate = DateTime.UtcNow
                    },
                    new FollowUp
                    {
                        LeadId = lead1?.LeadId,
                        Subject = "Follow-up Call on Zenith Hospital Demo",
                        FollowUpType = "Call",
                        FollowUpDate = DateTime.UtcNow.Date.AddDays(3),
                        Status = "Planned",
                        Remarks = "Check feedback on integration architecture document.",
                        AssignedTo = salesUser1.Id,
                        CreatedDate = DateTime.UtcNow
                    },
                    new FollowUp
                    {
                        OpportunityId = opp1?.OpportunityId,
                        Subject = "Send Revised Quotation",
                        FollowUpType = "Email",
                        FollowUpDate = DateTime.UtcNow.Date.AddDays(2),
                        Status = "Planned",
                        Remarks = "Email revised discounts approved by manager.",
                        AssignedTo = salesUser1.Id,
                        CreatedDate = DateTime.UtcNow
                    }
                };

                context.FollowUps.AddRange(followups);
                await context.SaveChangesAsync();
            }

            // 7. Seed Activities
            if (!context.Activities.Any())
            {
                var cust1 = context.Customers.FirstOrDefault();
                var lead1 = context.Leads.FirstOrDefault();

                var activities = new List<Activity>
                {
                    new Activity
                    {
                        ActivityType = "Call",
                        Subject = "Discovery call with VP Engineering",
                        Description = "Identified key requirements for multi-region CRM sync.",
                        ActivityDate = DateTime.UtcNow.AddDays(-2),
                        CustomerId = cust1?.CustomerId,
                        AssignedTo = salesUser1.Id,
                        Status = "Completed"
                    },
                    new Activity
                    {
                        ActivityType = "Meeting",
                        Subject = "Product Demo Showcase",
                        Description = "Presented 45-minute live demo of AcxiomCRM features.",
                        ActivityDate = DateTime.UtcNow.AddDays(-4),
                        LeadId = lead1?.LeadId,
                        AssignedTo = salesUser1.Id,
                        Status = "Completed"
                    }
                };

                context.Activities.AddRange(activities);
                await context.SaveChangesAsync();
            }

            // 8. Seed Audit Logs
            if (!context.AuditLogs.Any())
            {
                var logs = new List<AuditLog>
                {
                    new AuditLog
                    {
                        UserId = adminUser.Id,
                        Action = "Security",
                        EntityName = "Auth",
                        RecordId = "System",
                        Result = "Success",
                        Details = "System initialized with baseline roles and seed data.",
                        CreatedDate = DateTime.UtcNow.AddDays(-60),
                        IpAddress = "127.0.0.1"
                    },
                    new AuditLog
                    {
                        UserId = adminUser.Id,
                        Action = "Login",
                        EntityName = "Auth",
                        RecordId = adminUser.Email,
                        Result = "Success",
                        Details = "User admin@acxiomcrm.com logged in successfully.",
                        CreatedDate = DateTime.UtcNow.AddMinutes(-30),
                        IpAddress = "127.0.0.1"
                    }
                };

                context.AuditLogs.AddRange(logs);
                await context.SaveChangesAsync();
            }
        }
    }
}
