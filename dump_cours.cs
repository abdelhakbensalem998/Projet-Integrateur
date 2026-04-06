using System;
using System.Linq;
using GestionHoraire.Data;
using GestionHoraire.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GestionHoraire;Trusted_Connection=True;MultipleActiveResultSets=true"));

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

var cours = context.Cours.ToList();
foreach (var c in cours)
{
    Console.WriteLine($"ID: {c.Id}, Titre: {c.Titre}, UserID: {c.UtilisateurId}, ProfIDs: {c.ProfesseurIds}, SalleID: {c.SalleId}, GroupeIDs: {c.GroupeIds}");
}
