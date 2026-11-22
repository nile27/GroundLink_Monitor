using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GroundLink.Models;

// 텔레메트리 1건 (위성 ID, 측정 시각, 배터리 %, 온도 °C)
public record Telemetry(string SatelliteId, DateTime RecordedAt, double Battery, double Temperature);

