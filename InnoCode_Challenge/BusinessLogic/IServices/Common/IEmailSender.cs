﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.IServices.Common
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody);
    }
}
