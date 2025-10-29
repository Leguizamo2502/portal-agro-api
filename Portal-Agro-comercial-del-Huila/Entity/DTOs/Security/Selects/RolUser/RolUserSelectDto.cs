﻿using Entity.DTOs.BaseDTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.DTOs.Security.Selects.RolUser
{
    public class RolUserSelectDto : BaseDto
    {
        public int RolId { get; set; }
        public string RolName { get; set; }
        public string UserName { get; set; }
        public int UserId { get; set; }
    }
}
