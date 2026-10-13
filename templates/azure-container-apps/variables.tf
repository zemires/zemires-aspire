variable "subscription_id" {
  type = string
}

variable "organization" {
  type = string
}

variable "workload" {
  type = string
}

variable "environment_code" {
  type    = string
  default = "d1"
}

variable "location" {
  type    = string
  default = "Switzerland North"
}

variable "image_tags" {
  type    = map(string)
  default = {}
}

