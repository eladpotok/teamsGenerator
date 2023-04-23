import { Card, DatePicker, Form, Input, Select } from "antd";
import { QuestionCircleOutlined } from "@ant-design/icons";
import { useContext, useEffect, useRef, useState } from "react";
import { ConfigurationContext } from "../../Store/ConfigurationContext";
import dayjs from 'dayjs';
import { BrowserView, MobileView } from "react-device-detect";
import { AnalyticsContext } from "../../Store/AnalyticsContext";
import { UserContext } from "../../Store/UserContext";

const { Option } = Select;

function Config(props) {
    const analyticsContext = useContext(AnalyticsContext)
    const userContext = useContext(UserContext)
    const configContext = useContext(ConfigurationContext);
    useEffect(() => {
        (async () => {
            if (!configContext.userConfig) {
                configContext.setUserConfig({
                    eventDate: dayjs(new Date()),
                    shirtsColors: props.shirtsColors.slice(0, 3),
                    numberOfTeams: props.numberOfTeams,
                    algo: props.currentAlgo.algoKey
                })
            }
        })()
    }, [configContext.userConfig])

    const layout = {
        labelCol: { span: 10 },
        wrapperCol: { span: 16 },
    };

    function shirtsColorsChangedHandler(e) {
        configContext.userConfig.shirtsColors = e
        configContext.setUserConfig({ ...configContext.userConfig, userConfig: { ...configContext.userConfig } })

    }

    function setNumberOfTeams(teamsCount) {
        configContext.userConfig.numberOfTeams = teamsCount
        configContext.setUserConfig({ ...configContext.userConfig, userConfig: { ...configContext.userConfig } })
    }

    function setEventDate(e, dateValue) {
        configContext.userConfig.eventDate = e
        configContext.setUserConfig({ ...configContext.userConfig, userConfig: { ...configContext.userConfig } })
    }

    analyticsContext.sendAnalyticsImpression(userContext.user.uid, 'config')


    return (

        <Card title='Configuration' style={{backgroundColor: "white"}}>
            <QuestionCircleOutlined />  <label style={{marginLeft: '4px' }}>Setup your settings of your teams</label>
                {configContext.userConfig && <Form  {...layout} style={{ marginTop: '15px' }} size="small">

                    <Form.Item name="teamsCount" label="Number of Teams" >
                        <Input placeholder="Number of Teams" type="number" min={1} max={4} defaultValue={configContext.userConfig.numberOfTeams}  onChange={(e) => { setNumberOfTeams(e.target.value) }} />
                    </Form.Item>

                   <Form.Item name="shirtsColors" label="Shirts Colors" >
                        <Select mode="multiple" placeholder="Select Shirts Colors" defaultValue={configContext.userConfig.shirtsColors} onChange={shirtsColorsChangedHandler} >
                            {props.shirtsColors.map(shirt => {
                                return <Option value={shirt} label={shirt} />
                            })}
                        </Select>
                    </Form.Item>

                    <Form.Item name="eventDate" label="Event Date">
                        <DatePicker onChange={setEventDate} format="YYYY-MM-DD HH:mm:ss" showTime={{ defaultValue: dayjs(new Date())}}  defaultValue={configContext.userConfig.eventDate}/>
                    </Form.Item>
                </Form>}
        </Card>
    )

}


export default Config;