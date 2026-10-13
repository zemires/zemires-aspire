resource "random_string" "unique" {
  length  = 3
  lower   = true
  upper   = false
  special = false
  numeric = false
}

locals {
  name_template              = "${var.organization}-${var.environment_code}-<service>-${var.workload}"
  name_template_unique       = "${var.organization}-${var.environment_code}-<service>-${var.workload}-${random_string.unique.result}"
  name_template_short        = "${var.organization}${var.environment_code}<service>${substr(var.workload, 0, 3)}"
  name_template_short_unique = "${var.organization}${var.environment_code}<service>${substr(var.workload, 0, 3)}${random_string.unique.result}"
  tags = {
    workload     = var.workload
    environment = var.environment_code
  }
}

data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "app" {
  name     = replace(local.name_template, "<service>", "rg")
  location = var.location
  tags     = local.tags
}

resource "azurerm_user_assigned_identity" "app" {
  name                = replace(local.name_template, "<service>", "uai")
  resource_group_name = azurerm_resource_group.app.name
  location            = var.location
}
