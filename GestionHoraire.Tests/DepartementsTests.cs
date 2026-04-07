using System;
using System.Linq;
using System.Threading.Tasks;
using GestionHoraire.Controllers;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionHoraire.Tests
{
    public class DepartementsTests
    {
        private AppDbContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        private static DepartementsController GetController(AppDbContext context)
        {
            return new DepartementsController(context);
        }

        [Fact]
        public async Task Create_ShouldRejectDuplicateName_IgnoringCaseAndSpaces()
        {
            using var context = GetDbContext();
            context.Departements.Add(new Departement { Id = 1, Nom = "Informatique" });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.Create(new Departement { Id = 2, Nom = "  informatique  " });

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Departement>(viewResult.Model);

            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState[nameof(Departement.Nom)]!.Errors, e => e.ErrorMessage.Contains("existe déjà"));
            Assert.Equal("informatique", model.Nom, ignoreCase: true);
            Assert.Single(context.Departements.ToList());
        }

        [Fact]
        public async Task Edit_ShouldRejectDuplicateName_IgnoringCaseAndSpaces()
        {
            using var context = GetDbContext();
            context.Departements.AddRange(
                new Departement { Id = 1, Nom = "Informatique" },
                new Departement { Id = 2, Nom = "Mathématiques" });
            await context.SaveChangesAsync();

            var controller = GetController(context);

            var result = await controller.Edit(2, new Departement { Id = 2, Nom = "  INFORMATIQUE " });

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(controller.ModelState[nameof(Departement.Nom)]!.Errors, e => e.ErrorMessage.Contains("existe déjà"));

            var departement = await context.Departements.FindAsync(2);
            Assert.NotNull(departement);
            Assert.Equal("Mathématiques", departement!.Nom);
        }
    }
}
